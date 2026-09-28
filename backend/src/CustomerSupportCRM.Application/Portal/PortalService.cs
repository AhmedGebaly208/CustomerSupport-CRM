using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Portal;

public interface IPortalService
{
    Task<PortalProfileDto> GetProfileAsync(CancellationToken ct = default);

    Task<PagedResult<PortalTicketListItemDto>> ListTicketsAsync(
        PortalTicketQuery query, CancellationToken ct = default);

    Task<PortalTicketDto> GetTicketAsync(Guid ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<PortalMessageDto>> GetMessagesAsync(Guid ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<PortalAttachmentDto>> GetAttachmentsAsync(Guid ticketId, CancellationToken ct = default);

    Task<PortalTicketDto> CreateTicketAsync(CreatePortalTicketRequest request, CancellationToken ct = default);
    Task<PortalMessageDto> ReplyAsync(Guid ticketId, PortalReplyRequest request, CancellationToken ct = default);
    Task<PortalTicketDto> CloseTicketAsync(Guid ticketId, CancellationToken ct = default);
}

/// <summary>The customer-facing portal (PDF area 9).
///
/// The security boundary is the point of this service, so it is built to make a leak
/// structurally hard rather than merely unlikely:
///
/// <list type="bullet">
/// <item>The customer is resolved from the signed-in user's id, never from a parameter. There
/// is no argument a caller could tamper with to address someone else's record.</item>
/// <item>Every query starts from that customer id. A ticket is loaded by
/// <c>(id AND customerId)</c>, so a guessed id reads as missing rather than forbidden — which
/// also avoids confirming that it exists.</item>
/// <item>Internal comments are excluded in the query, not filtered afterwards.</item>
/// <item>It returns portal DTOs that have no property for an agent name, a department, an
/// escalation level or an SLA date, so those cannot leak even by mistake.</item>
/// </list>
///
/// It deliberately shares no code path with the staff services.</summary>
public sealed class PortalService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IReferenceNumberGenerator numbers,
    IClock clock) : IPortalService
{
    /// <summary>How many open tickets one customer may have at once. A portal with no cap is
    /// a way to flood the desk, and a customer with fifty open tickets is not being served.</summary>
    private const int MaxOpenTickets = 20;

    public async Task<PortalProfileDto> GetProfileAsync(CancellationToken ct = default)
    {
        var customer = await RequireCustomerAsync(ct);

        return new PortalProfileDto(
            customer.Id, customer.Code, customer.FullNameAr, customer.FullNameEn,
            customer.Email, customer.Phone, customer.PreferredLanguage ?? "ar");
    }

    public async Task<PagedResult<PortalTicketListItemDto>> ListTicketsAsync(
        PortalTicketQuery query, CancellationToken ct = default)
    {
        var customerId = await RequireCustomerIdAsync(ct);

        var q = db.Tickets.AsNoTracking().Where(t => t.CustomerId == customerId);

        if (query.OpenOnly)
        {
            var open = TicketWorkflow.ActiveStatuses;
            q = q.Where(t => open.Contains(t.Status));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt)
            .Skip(query.Skip)
            .Take(Math.Clamp(query.PageSize, 1, 50))
            .Select(t => new PortalTicketListItemDto(
                t.Id, t.Number, t.Subject, t.Status, t.Priority,
                t.CreatedAt, t.ModifiedAt ?? t.CreatedAt))
            .ToListAsync(ct);

        return PagedResult<PortalTicketListItemDto>.Create(items, total, query.Page, query.PageSize);
    }

    public async Task<PortalTicketDto> GetTicketAsync(Guid ticketId, CancellationToken ct = default)
    {
        var customerId = await RequireCustomerIdAsync(ct);
        var ticket = await OwnTicketAsync(ticketId, customerId, ct);

        return await ToDtoAsync(ticket, ct);
    }

    public async Task<IReadOnlyList<PortalMessageDto>> GetMessagesAsync(
        Guid ticketId, CancellationToken ct = default)
    {
        var customerId = await RequireCustomerIdAsync(ct);

        // Ownership is established before any message is read.
        await OwnTicketAsync(ticketId, customerId, ct);

        return await db.TicketComments.AsNoTracking()
            // Excluded in the predicate, not filtered after: a later change to the projection
            // cannot accidentally let an internal note through.
            .Where(c => c.TicketId == ticketId && !c.IsInternal)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new PortalMessageDto(
                c.Id,
                c.Body,
                // Written by staff unless the customer's own portal user wrote it.
                c.AuthorId != currentUser.UserId,
                c.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PortalAttachmentDto>> GetAttachmentsAsync(
        Guid ticketId, CancellationToken ct = default)
    {
        var customerId = await RequireCustomerIdAsync(ct);
        await OwnTicketAsync(ticketId, customerId, ct);

        // Only files on the ticket itself. Attachments hanging off internal comments are not
        // reachable from here, because their owner id is the comment, not the ticket.
        return await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == AttachmentOwnerType.Ticket && a.OwnerId == ticketId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new PortalAttachmentDto(
                a.Id, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<PortalTicketDto> CreateTicketAsync(
        CreatePortalTicketRequest request, CancellationToken ct = default)
    {
        var customer = await RequireCustomerAsync(ct);

        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new BadRequestException("A ticket needs a subject.");

        if (string.IsNullOrWhiteSpace(request.Description))
            throw new BadRequestException("A ticket needs a description.");

        var open = TicketWorkflow.ActiveStatuses;

        var openCount = await db.Tickets
            .CountAsync(t => t.CustomerId == customer.Id && open.Contains(t.Status), ct);

        if (openCount >= MaxOpenTickets)
        {
            throw new ConflictException(
                "You already have the maximum number of open requests. Please reply on an existing one.",
                ErrorCodes.PortalTooManyOpenTickets);
        }

        // The category is checked against the active, published set rather than trusted, so
        // a customer cannot file into an internal-only category by guessing its id.
        if (request.CategoryId is { } categoryId
            && !await db.TicketCategories.AnyAsync(c => c.Id == categoryId && c.IsActive, ct))
        {
            throw new BadRequestException("The selected category is not available.");
        }

        var now = clock.UtcNow;

        var ticket = new Ticket
        {
            Number = await numbers.NextTicketNumberAsync(ct),
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            CustomerId = customer.Id,
            CategoryId = request.CategoryId,
            // Priority is a desk judgement. A customer's sense of urgency is recorded but not
            // allowed to jump the queue, so anything above High is clamped.
            Priority = request.Priority > TicketPriority.High ? TicketPriority.High : request.Priority,
            Channel = CommunicationChannel.Portal,
            Status = TicketStatus.New,
            DepartmentId = customer.DepartmentId,
            BranchId = customer.BranchId,
            CreatedAt = now
        };

        db.Tickets.Add(ticket);

        db.TicketHistory.Add(new TicketHistory
        {
            TicketId = ticket.Id,
            Field = "Created",
            NewValue = ticket.Number,
            Note = "Raised in the customer portal",
            ChangedAt = now
        });

        db.Interactions.Add(new Interaction
        {
            CustomerId = customer.Id,
            TicketId = ticket.Id,
            Channel = CommunicationChannel.Portal,
            Direction = InteractionDirection.Inbound,
            Subject = ticket.Subject,
            Body = ticket.Description,
            OccurredAt = now
        });

        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(ticket, ct);
    }

    public async Task<PortalMessageDto> ReplyAsync(
        Guid ticketId, PortalReplyRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            throw new BadRequestException("A reply cannot be empty.");

        var customerId = await RequireCustomerIdAsync(ct);
        var ticket = await OwnTicketAsync(ticketId, customerId, ct);

        var now = clock.UtcNow;

        var comment = new TicketComment
        {
            TicketId = ticket.Id,
            Body = request.Body.Trim(),
            // Never internal. A customer cannot write a note the desk hides from them, and
            // more importantly cannot write into the internal channel at all.
            IsInternal = false,
            AuthorId = currentUser.UserId
        };

        db.TicketComments.Add(comment);

        db.Interactions.Add(new Interaction
        {
            CustomerId = customerId,
            TicketId = ticket.Id,
            Channel = CommunicationChannel.Portal,
            Direction = InteractionDirection.Inbound,
            Subject = ticket.Subject,
            Body = comment.Body,
            OccurredAt = now
        });

        // A reply to something the desk considered finished brings it back. Anything still
        // live is left alone, so replying does not reset an agent's working state.
        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            db.TicketHistory.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                Field = nameof(Ticket.Status),
                OldValue = ticket.Status.ToString(),
                NewValue = TicketStatus.Reopened.ToString(),
                Note = "Customer replied in the portal",
                ChangedAt = now
            });

            ticket.Status = TicketStatus.Reopened;
            ticket.ResolvedAt = null;
        }

        await db.SaveChangesAsync(ct);

        return new PortalMessageDto(comment.Id, comment.Body, false, comment.CreatedAt);
    }

    public async Task<PortalTicketDto> CloseTicketAsync(Guid ticketId, CancellationToken ct = default)
    {
        var customerId = await RequireCustomerIdAsync(ct);
        var ticket = await OwnTicketAsync(ticketId, customerId, ct);

        if (ticket.Status == TicketStatus.Closed) return await ToDtoAsync(ticket, ct);

        if (!TicketWorkflow.CanTransition(ticket.Status, TicketStatus.Closed))
        {
            throw new ConflictException(
                $"A request in '{ticket.Status}' cannot be closed.", ErrorCodes.PortalCannotClose);
        }

        var now = clock.UtcNow;

        db.TicketHistory.Add(new TicketHistory
        {
            TicketId = ticket.Id,
            Field = nameof(Ticket.Status),
            OldValue = ticket.Status.ToString(),
            NewValue = TicketStatus.Closed.ToString(),
            Note = "Closed by the customer in the portal",
            ChangedAt = now
        });

        ticket.Status = TicketStatus.Closed;
        ticket.ClosedAt = now;

        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(ticket, ct);
    }

    // ---- boundary ----

    /// <summary>The customer behind the signed-in user.
    ///
    /// Resolved from the token's user id and nothing else. There is no customer id parameter
    /// anywhere in this service's surface, so there is nothing for a caller to tamper with.
    /// A portal user not linked to a customer record is refused rather than defaulted to
    /// anything.</summary>
    private async Task<Customer> RequireCustomerAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId
            ?? throw new ForbiddenException("Not authenticated.", ErrorCodes.NotAuthenticated);

        return await db.Customers.FirstOrDefaultAsync(c => c.UserId == userId, ct)
            ?? throw new ForbiddenException(
                "This account is not linked to a customer record.", ErrorCodes.PortalNotLinked);
    }

    private async Task<Guid> RequireCustomerIdAsync(CancellationToken ct) =>
        (await RequireCustomerAsync(ct)).Id;

    /// <summary>A ticket, but only if it belongs to this customer.
    ///
    /// The ownership test is in the predicate, so someone else's ticket comes back as missing
    /// rather than forbidden — a 403 would confirm the id exists, which is itself a leak.</summary>
    private async Task<Ticket> OwnTicketAsync(Guid ticketId, Guid customerId, CancellationToken ct) =>
        await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId && t.CustomerId == customerId, ct)
        ?? throw new NotFoundException(nameof(Ticket), ticketId);

    private async Task<PortalTicketDto> ToDtoAsync(Ticket ticket, CancellationToken ct)
    {
        var category = ticket.CategoryId is { } categoryId
            ? await db.TicketCategories.AsNoTracking()
                .Where(c => c.Id == categoryId)
                .Select(c => new { c.NameAr, c.NameEn })
                .FirstOrDefaultAsync(ct)
            : null;

        var score = await db.TicketSatisfaction.AsNoTracking()
            .Where(s => s.TicketId == ticket.Id)
            .Select(s => (int?)s.Score)
            .FirstOrDefaultAsync(ct);

        var finished = ticket.Status is TicketStatus.Resolved or TicketStatus.Closed;

        return new PortalTicketDto(
            ticket.Id, ticket.Number, ticket.Subject, ticket.Description,
            ticket.Status, ticket.Priority, ticket.Channel,
            category?.NameAr, category?.NameEn,
            ticket.CreatedAt, ticket.ResolvedAt, ticket.ClosedAt,
            // Replying is always possible: it is how a customer reopens something that was
            // closed too early.
            CanReply: true,
            CanRate: finished && score is null,
            SatisfactionScore: score);
    }
}
