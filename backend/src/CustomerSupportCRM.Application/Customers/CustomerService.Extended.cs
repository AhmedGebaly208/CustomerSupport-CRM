using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Customers.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Customers;

/// <summary>The customer 360° view (PDF area 1): file attachments, merging duplicate
/// records, bulk import, and a consolidated activity timeline.
///
/// Split from the CRUD half in CustomerService.cs to keep each file readable. Every method
/// here goes through the same scope checks as the CRUD paths.</summary>
public sealed partial class CustomerService
{
    /// <summary>Rows written per SaveChanges during an import. Small enough that a failure
    /// loses little, large enough that a thousand-row file is not a thousand round-trips.</summary>
    private const int ImportBatchSize = 100;

    // ---- Attachments ----

    public async Task<AttachmentDetailDto> AddAttachmentAsync(
        AttachmentOwnerType ownerType, Guid ownerId, Stream content, string fileName,
        string? contentType, CancellationToken ct = default)
    {
        await EnsureOwnerAccessibleAsync(ownerType, ownerId, ct);

        // LocalFileStorage enforces the allow-list and renames the file on disk; it throws
        // InvalidOperationException for a disallowed type, which would otherwise surface as
        // a 500. Translate it into the 400 the caller deserves.
        string storagePath;
        try
        {
            storagePath = await storage.SaveAsync(content, fileName, $"{ownerType}/{ownerId:N}", ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        var attachment = new Attachment
        {
            OwnerType = ownerType,
            OwnerId = ownerId,
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeBytes = content.CanSeek ? content.Length : 0,
            StoragePath = storagePath
        };

        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        return (await ToAttachmentDtosAsync([attachment], ct))[0];
    }

    public async Task<IReadOnlyList<AttachmentDetailDto>> ListAttachmentsAsync(
        AttachmentOwnerType ownerType, Guid ownerId, CancellationToken ct = default)
    {
        await EnsureOwnerAccessibleAsync(ownerType, ownerId, ct);

        var rows = await db.Attachments.AsNoTracking()
            .Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        return await ToAttachmentDtosAsync(rows, ct);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> OpenAttachmentAsync(
        AttachmentOwnerType ownerType, Guid ownerId, Guid attachmentId, CancellationToken ct = default)
    {
        await EnsureOwnerAccessibleAsync(ownerType, ownerId, ct);

        var attachment = await db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId
                                      && a.OwnerType == ownerType
                                      && a.OwnerId == ownerId, ct)
            ?? throw new NotFoundException(nameof(Attachment), attachmentId);

        var stream = await storage.OpenAsync(attachment.StoragePath, ct)
            // The row exists but the bytes do not: a partially restored backup, or a file
            // removed outside the app. A 404 is honest; a 500 would suggest a bug.
            ?? throw new NotFoundException("Attachment file", attachment.FileName);

        return (stream, attachment.ContentType, attachment.FileName);
    }

    public async Task DeleteAttachmentAsync(
        AttachmentOwnerType ownerType, Guid ownerId, Guid attachmentId, CancellationToken ct = default)
    {
        await EnsureOwnerAccessibleAsync(ownerType, ownerId, ct);

        var attachment = await db.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId
                                      && a.OwnerType == ownerType
                                      && a.OwnerId == ownerId, ct)
            ?? throw new NotFoundException(nameof(Attachment), attachmentId);

        // Soft delete only. The bytes stay on disk deliberately: the audit trail records
        // that a file was removed, and an auditor who needs to see what it was should be
        // able to. Purging orphaned blobs is a retention job, not a delete-time action.
        attachment.IsDeleted = true;
        attachment.DeletedAt = clock.UtcNow;
        attachment.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Attachments hang off customers, tickets and ticket comments. Each owner is
    /// resolved to its customer or ticket so the existing scope check applies — otherwise
    /// an attachment id would be a way around department scoping.</summary>
    private async Task EnsureOwnerAccessibleAsync(AttachmentOwnerType ownerType, Guid ownerId, CancellationToken ct)
    {
        switch (ownerType)
        {
            case AttachmentOwnerType.Customer:
                await EnsureCustomerExistsAsync(ownerId, ct);
                return;

            case AttachmentOwnerType.Ticket:
            {
                var ticket = await db.Tickets.AsNoTracking()
                    .Where(t => t.Id == ownerId)
                    .Select(t => new ScopeCheck(t.DepartmentId, t.BranchId))
                    .FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException(nameof(Ticket), ownerId);

                scope.EnsureCanAccess(ticket);
                return;
            }

            case AttachmentOwnerType.TicketComment:
            {
                var parent = await db.TicketComments.AsNoTracking()
                    .Where(c => c.Id == ownerId)
                    .Select(c => new ScopeCheck(c.Ticket!.DepartmentId, c.Ticket.BranchId))
                    .FirstOrDefaultAsync(ct)
                    ?? throw new NotFoundException(nameof(TicketComment), ownerId);

                scope.EnsureCanAccess(parent);
                return;
            }

            case AttachmentOwnerType.Interaction:
            {
                var interaction = await db.Interactions.AsNoTracking()
                    .Where(i => i.Id == ownerId)
                    .Select(i => i.CustomerId)
                    .FirstOrDefaultAsync(ct);

                if (interaction == Guid.Empty)
                    throw new NotFoundException(nameof(Interaction), ownerId);

                await EnsureCustomerExistsAsync(interaction, ct);
                return;
            }

            default:
                throw new BadRequestException($"Unsupported attachment owner type '{ownerType}'.");
        }
    }

    private async Task<IReadOnlyList<AttachmentDetailDto>> ToAttachmentDtosAsync(
        IReadOnlyList<Attachment> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var names = await ResolveNamesAsync(rows.Select(a => a.CreatedBy), ct);

        return rows.Select(a => new AttachmentDetailDto(
            a.Id, a.OwnerType, a.OwnerId, a.FileName, a.ContentType, a.SizeBytes,
            a.CreatedBy, Lookup(names, a.CreatedBy), a.CreatedAt)).ToList();
    }

    // ---- Merge ----

    /// <summary>Folds one customer record into another.
    ///
    /// Everything the loser owns is re-pointed at the survivor in a single transaction, so
    /// a failure part-way cannot leave a customer's history split across two records. The
    /// loser is then soft-deleted with an audit entry naming the survivor, which is what
    /// makes the operation traceable rather than merely destructive.</summary>
    public async Task<CustomerMergeResultDto> MergeAsync(
        CustomerMergeRequest request, CancellationToken ct = default)
    {
        if (request.SurvivorId == request.LoserId)
            throw new BadRequestException("A customer cannot be merged into itself.");

        // Validate up front so a bad request fails without opening a transaction. The
        // entities are deliberately re-read inside it: the runner clears the change tracker
        // per attempt, so anything loaded out here would be detached and its mutations lost.
        var preflightSurvivor = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.SurvivorId, ct)
            ?? throw new NotFoundException(nameof(Customer), request.SurvivorId);
        scope.EnsureCanAccess(preflightSurvivor);

        var preflightLoser = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.LoserId, ct)
            ?? throw new NotFoundException(nameof(Customer), request.LoserId);
        scope.EnsureCanAccess(preflightLoser);

        // The one thing that genuinely cannot be merged: two portal logins. A customer row
        // carries at most one UserId, so merging would silently orphan one person's access.
        // Active tickets on both sides are *not* a conflict — that is the normal case for
        // duplicates, and refusing it would make the feature useless.
        if (preflightSurvivor.UserId is not null && preflightLoser.UserId is not null)
        {
            throw new ConflictException(
                "Both customers have a portal login. Remove one login before merging, "
                + "otherwise one of them would lose access.");
        }

        var result = await RunInTransactionAsync(async () =>
        {
            var survivor = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.SurvivorId, ct)
                ?? throw new NotFoundException(nameof(Customer), request.SurvivorId);

            var loser = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.LoserId, ct)
                ?? throw new NotFoundException(nameof(Customer), request.LoserId);

            var tickets = await db.Tickets.Where(t => t.CustomerId == loser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CustomerId, survivor.Id), ct);

            var interactions = await db.Interactions.Where(i => i.CustomerId == loser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CustomerId, survivor.Id), ct);

            var notes = await db.CustomerNotes.Where(n => n.CustomerId == loser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.CustomerId, survivor.Id), ct);

            var contacts = await db.CustomerContacts.Where(c => c.CustomerId == loser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CustomerId, survivor.Id), ct);

            var attachments = await db.Attachments
                .Where(a => a.OwnerType == AttachmentOwnerType.Customer && a.OwnerId == loser.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.OwnerId, survivor.Id), ct);

            // Carry across anything the survivor is missing, so the merge does not lose
            // detail that only the duplicate happened to have.
            survivor.Email ??= loser.Email;
            survivor.Phone ??= loser.Phone;
            survivor.WhatsAppNumber ??= loser.WhatsAppNumber;
            survivor.CompanyName ??= loser.CompanyName;
            survivor.NationalId ??= loser.NationalId;
            survivor.Address ??= loser.Address;
            survivor.DepartmentId ??= loser.DepartmentId;
            survivor.BranchId ??= loser.BranchId;
            survivor.UserId ??= loser.UserId;

            loser.IsDeleted = true;
            loser.DeletedAt = clock.UtcNow;
            loser.DeletedBy = currentUser.UserId;

            // The interceptor records the individual row updates, but not the intent. This
            // explicit entry is what answers "where did customer CUS-000042 go?".
            db.AuditLogs.Add(new AuditLog
            {
                EntityName = nameof(Customer),
                EntityId = survivor.Id.ToString(),
                Action = AuditAction.Merged,
                Changes = System.Text.Json.JsonSerializer.Serialize(new
                {
                    mergedFrom = loser.Id,
                    mergedFromCode = loser.Code,
                    reason = request.Reason,
                    tickets,
                    interactions,
                    notes,
                    contacts,
                    attachments
                }),
                UserId = currentUser.UserId,
                UserName = currentUser.UserName,
                OccurredAt = clock.UtcNow
            });

            await db.SaveChangesAsync(ct);

            return new CustomerMergeResultDto(
                survivor.Id, loser.Id, tickets, interactions, notes, contacts, attachments);
        });

        return result;
    }

    // ---- Import ----

    /// <summary>Bulk-creates customers from a spreadsheet.
    ///
    /// Every row is validated with the same validator the single-create endpoint uses, and
    /// a failure is reported against its row number rather than aborting the run — an
    /// operator importing four hundred customers should not lose the file because row 87
    /// has a malformed email.</summary>
    public async Task<CustomerImportResultDto> ImportAsync(
        Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        var parser = importParsers.FirstOrDefault(p => p.CanParse(fileName, contentType))
            ?? throw new BadRequestException("Unsupported file type. Upload a .csv or .xlsx file.");

        IReadOnlyList<CustomerImportRow> rows;
        try
        {
            rows = await parser.ParseAsync(content, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A corrupt or password-protected workbook is the caller's problem, not a 500.
            throw new BadRequestException($"The file could not be read: {ex.Message}");
        }

        if (rows.Count == 0)
            throw new BadRequestException(
                "No rows were found. Check that the first row holds column headers such as "
                + "FullNameEn, FullNameAr, Email and Phone.");

        var departments = await db.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase, ct);

        var branches = await db.Branches.AsNoTracking()
            .ToDictionaryAsync(b => b.Code, b => b.Id, StringComparer.OrdinalIgnoreCase, ct);

        var results = new List<CustomerImportRowResultDto>(rows.Count);
        var pending = new List<(Customer Entity, int RowNumber)>();

        // Emails already taken, tracked in memory so a duplicate *within the file* is
        // caught as well as one against the database.
        var seenEmails = new HashSet<string>(
            await db.Customers.AsNoTracking()
                .Where(c => c.Email != null)
                .Select(c => c.Email!)
                .ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var errors = new List<string>();

            var request = new CreateCustomerRequest(
                row.FullNameAr ?? string.Empty,
                row.FullNameEn ?? string.Empty,
                row.Email, row.Phone, row.WhatsAppNumber, row.CompanyName,
                row.NationalId, row.Address,
                row.PreferredLanguage is "en" or "ar" ? row.PreferredLanguage : "ar",
                ResolveCode(row.DepartmentCode, departments, "department", errors),
                ResolveCode(row.BranchCode, branches, "branch", errors),
                null);

            var validation = await importValidator.ValidateAsync(request, ct);
            errors.AddRange(validation.Errors.Select(e => e.ErrorMessage));

            if (!string.IsNullOrWhiteSpace(request.Email) && !seenEmails.Add(request.Email))
                errors.Add($"The email '{request.Email}' is already in use.");

            if (errors.Count > 0)
            {
                results.Add(new CustomerImportRowResultDto(row.RowNumber, false, null, null, errors));
                continue;
            }

            var customer = new Customer
            {
                Code = await numbers.NextCustomerCodeAsync(ct),
                FullNameAr = request.FullNameAr.Trim(),
                FullNameEn = request.FullNameEn.Trim(),
                Email = request.Email?.Trim(),
                Phone = request.Phone?.Trim(),
                WhatsAppNumber = request.WhatsAppNumber?.Trim(),
                CompanyName = request.CompanyName?.Trim(),
                NationalId = request.NationalId?.Trim(),
                Address = request.Address?.Trim(),
                PreferredLanguage = request.PreferredLanguage,
                DepartmentId = request.DepartmentId,
                BranchId = request.BranchId,
                IsActive = true
            };

            db.Customers.Add(customer);
            pending.Add((customer, row.RowNumber));

            if (pending.Count >= ImportBatchSize)
            {
                await db.SaveChangesAsync(ct);
                results.AddRange(pending.Select(p =>
                    new CustomerImportRowResultDto(p.RowNumber, true, p.Entity.Id, p.Entity.Code, [])));
                pending.Clear();
            }
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            results.AddRange(pending.Select(p =>
                new CustomerImportRowResultDto(p.RowNumber, true, p.Entity.Id, p.Entity.Code, [])));
        }

        var ordered = results.OrderBy(r => r.RowNumber).ToList();
        var succeeded = ordered.Count(r => r.Succeeded);

        db.AuditLogs.Add(new AuditLog
        {
            EntityName = nameof(Customer),
            EntityId = "import",
            Action = AuditAction.Imported,
            Changes = System.Text.Json.JsonSerializer.Serialize(new
            {
                fileName,
                totalRows = ordered.Count,
                succeeded,
                failed = ordered.Count - succeeded
            }),
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            OccurredAt = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);

        return new CustomerImportResultDto(ordered.Count, succeeded, ordered.Count - succeeded, ordered);
    }

    private static Guid? ResolveCode(
        string? code, IReadOnlyDictionary<string, Guid> lookup, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        if (lookup.TryGetValue(code.Trim(), out var id)) return id;

        errors.Add($"Unknown {label} code '{code.Trim()}'.");
        return null;
    }

    // ---- Activity timeline ----

    /// <summary>One chronological feed across tickets, interactions, notes and attachments.
    ///
    /// Four separate queries unioned in memory rather than one SQL UNION: the sources have
    /// nothing in common structurally, and a union would force every column into a shared
    /// shape at the database. Each query is bounded by the page window first, so the number
    /// of rows pulled back stays proportional to the page, not to the customer's history.</summary>
    public async Task<PagedResult<CustomerActivityItemDto>> GetActivityAsync(
        Guid customerId, CustomerActivityQuery query, CancellationToken ct = default)
    {
        await EnsureCustomerExistsAsync(customerId, ct);

        var wanted = query.Types is { Length: > 0 }
            ? query.Types.ToHashSet()
            : Enum.GetValues<CustomerActivityType>().ToHashSet();

        // Each source contributes at most (skip + take) rows, which is the most that could
        // reach the requested page once everything is interleaved.
        var ceiling = query.Skip + query.PageSize;
        var items = new List<CustomerActivityItemDto>();

        if (wanted.Contains(CustomerActivityType.Ticket))
        {
            items.AddRange(await db.Tickets.AsNoTracking()
                .Where(t => t.CustomerId == customerId)
                .OrderByDescending(t => t.CreatedAt)
                .Take(ceiling)
                .Select(t => new CustomerActivityItemDto(
                    CustomerActivityType.Ticket, t.Id, t.CreatedAt,
                    t.Subject, t.Subject, t.Description, t.Number, t.Status.ToString(),
                    t.AssignedAgentId, null))
                .ToListAsync(ct));
        }

        if (wanted.Contains(CustomerActivityType.Interaction))
        {
            items.AddRange(await db.Interactions.AsNoTracking()
                .Where(i => i.CustomerId == customerId)
                .OrderByDescending(i => i.OccurredAt)
                .Take(ceiling)
                .Select(i => new CustomerActivityItemDto(
                    CustomerActivityType.Interaction, i.Id, i.OccurredAt,
                    i.Subject, i.Subject, i.Body,
                    i.Ticket != null ? i.Ticket.Number : null,
                    i.Direction.ToString(), i.AgentId, null))
                .ToListAsync(ct));
        }

        if (wanted.Contains(CustomerActivityType.Note))
        {
            items.AddRange(await db.CustomerNotes.AsNoTracking()
                .Where(n => n.CustomerId == customerId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(ceiling)
                .Select(n => new CustomerActivityItemDto(
                    CustomerActivityType.Note, n.Id, n.CreatedAt,
                    null, null, n.Body, null,
                    n.IsInternal ? "Internal" : "Shared", n.CreatedBy, null))
                .ToListAsync(ct));
        }

        if (wanted.Contains(CustomerActivityType.Attachment))
        {
            items.AddRange(await db.Attachments.AsNoTracking()
                .Where(a => a.OwnerType == AttachmentOwnerType.Customer && a.OwnerId == customerId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(ceiling)
                .Select(a => new CustomerActivityItemDto(
                    CustomerActivityType.Attachment, a.Id, a.CreatedAt,
                    a.FileName, a.FileName, a.ContentType, null, null, a.CreatedBy, null))
                .ToListAsync(ct));
        }

        var page = items
            .OrderByDescending(i => i.OccurredAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToList();

        // Actor names are resolved only for the page actually returned.
        var names = await ResolveNamesAsync(page.Select(i => i.ActorId), ct);
        var named = page.Select(i => i with { ActorName = Lookup(names, i.ActorId) }).ToList();

        // TotalCount is the exact count per source, so paging stays honest even though the
        // page itself was assembled from bounded slices.
        var total = await CountActivityAsync(customerId, wanted, ct);

        return PagedResult<CustomerActivityItemDto>.Create(named, total, query.Page, query.PageSize);
    }

    private async Task<int> CountActivityAsync(
        Guid customerId, IReadOnlySet<CustomerActivityType> wanted, CancellationToken ct)
    {
        var total = 0;

        if (wanted.Contains(CustomerActivityType.Ticket))
            total += await db.Tickets.CountAsync(t => t.CustomerId == customerId, ct);

        if (wanted.Contains(CustomerActivityType.Interaction))
            total += await db.Interactions.CountAsync(i => i.CustomerId == customerId, ct);

        if (wanted.Contains(CustomerActivityType.Note))
            total += await db.CustomerNotes.CountAsync(n => n.CustomerId == customerId, ct);

        if (wanted.Contains(CustomerActivityType.Attachment))
        {
            total += await db.Attachments.CountAsync(
                a => a.OwnerType == AttachmentOwnerType.Customer && a.OwnerId == customerId, ct);
        }

        return total;
    }
}
