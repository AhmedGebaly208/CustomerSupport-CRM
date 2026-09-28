using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Tickets;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Application.Channels;

/// <summary>Turns an arriving message into desk work (PDF area 3).
///
/// Three questions, in order: have we already seen this message, who sent it, and does it
/// belong on a ticket we already have. Getting the first wrong duplicates tickets, the
/// second scatters one customer across many records, and the third breaks a conversation
/// into unrelated fragments.</summary>
public sealed class InboundMessageProcessor(
    IAppDbContext db,
    ITicketService tickets,
    IFileStorage storage,
    IReferenceNumberGenerator numbers,
    IClock clock,
    ILogger<InboundMessageProcessor> logger) : IInboundMessageProcessor
{
    /// <summary>How long after its last activity a ticket still absorbs a message that
    /// carries no reference token. Beyond this, a customer writing in again is starting a
    /// new conversation, not continuing an old one.</summary>
    private static readonly TimeSpan RecentTicketWindow = TimeSpan.FromHours(72);

    public async Task<InboundResult> ProcessAsync(InboundMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // 1. Idempotency. Providers retry webhooks, and a retry must not raise a second
        // ticket. The unique index is what actually enforces this; the check just avoids
        // doing the work.
        var existing = await db.ChannelMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Channel == message.Channel
                                      && m.ProviderMessageId == message.ProviderMessageId, ct);

        if (existing is { TicketId: not null })
        {
            var ticketNumber = await db.Tickets.AsNoTracking()
                .Where(t => t.Id == existing.TicketId)
                .Select(t => t.Number)
                .FirstOrDefaultAsync(ct) ?? string.Empty;

            logger.LogInformation(
                "Inbound {Channel} message {ProviderId} already processed; ignoring the replay.",
                message.Channel, message.ProviderMessageId);

            return new InboundResult(
                existing.TicketId.Value, ticketNumber, existing.CustomerId ?? Guid.Empty,
                CreatedTicket: false, CreatedCustomer: false, Duplicate: true);
        }

        var now = clock.UtcNow;

        // 2. Who sent it.
        var (customer, createdCustomer) = await MatchCustomerAsync(message, ct);

        // 3. Where it belongs.
        var ticket = await FindThreadAsync(message, customer, ct);
        var createdTicket = ticket is null;

        if (ticket is null)
        {
            ticket = new Ticket
            {
                Number = await numbers.NextTicketNumberAsync(ct),
                Subject = BuildSubject(message),
                Description = message.BodyText,
                CustomerId = customer.Id,
                Channel = message.Channel,
                Status = TicketStatus.New,
                Priority = TicketPriority.Normal,
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
                Note = $"Received on {message.Channel}",
                ChangedAt = now
            });
        }
        else if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            // The customer came back about something we thought was finished. Reopening is
            // the honest reading, and the workflow allows it from both terminal states.
            var from = ticket.Status;
            ticket.Status = TicketStatus.Reopened;

            db.TicketHistory.Add(new TicketHistory
            {
                TicketId = ticket.Id,
                Field = nameof(Ticket.Status),
                OldValue = from.ToString(),
                NewValue = TicketStatus.Reopened.ToString(),
                Note = $"Customer replied on {message.Channel}",
                ChangedAt = now
            });
        }

        // 4. The message itself, recorded as a customer touchpoint.
        var interaction = new Interaction
        {
            CustomerId = customer.Id,
            TicketId = ticket.Id,
            Channel = message.Channel,
            Direction = InteractionDirection.Inbound,
            Subject = message.Subject,
            Body = message.BodyText,
            OccurredAt = message.ReceivedAt
        };

        db.Interactions.Add(interaction);

        db.ChannelMessages.Add(new ChannelMessage
        {
            Channel = message.Channel,
            Direction = ChannelDirection.Inbound,
            ProviderMessageId = message.ProviderMessageId,
            ProviderConversationId = message.ProviderConversationId,
            Address = AddressOf(message.From),
            Subject = message.Subject,
            BodyText = message.BodyText,
            // Inbound is already here, so there is no delivery to attempt.
            Status = ChannelMessageStatus.Delivered,
            OccurredAt = message.ReceivedAt,
            TicketId = ticket.Id,
            CustomerId = customer.Id,
            InteractionId = interaction.Id
        });

        await db.SaveChangesAsync(ct);

        await SaveAttachmentsAsync(message, interaction.Id, ct);

        logger.LogInformation(
            "Inbound {Channel} message threaded onto {TicketNumber} ({Outcome}).",
            message.Channel, ticket.Number, createdTicket ? "new ticket" : "existing ticket");

        return new InboundResult(
            ticket.Id, ticket.Number, customer.Id, createdTicket, createdCustomer, Duplicate: false);
    }

    // ---- customer matching ----

    /// <summary>Finds the customer this message came from, creating a skeleton record when
    /// nobody matches.
    ///
    /// Creating rather than rejecting is deliberate: a message from an unknown address is
    /// still a customer asking for help, and losing it would be worse than holding an
    /// incomplete profile for an agent to finish.</summary>
    private async Task<(Customer Customer, bool Created)> MatchCustomerAsync(
        InboundMessage message, CancellationToken ct)
    {
        var from = message.From;

        var email = Normalize(from.Email);
        var phone = NormalizePhone(from.Phone);
        var whatsapp = NormalizePhone(from.WhatsAppNumber);

        Customer? match = null;

        if (email is not null)
        {
            match = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct)
                ?? await MatchByContactAsync(email, ct);
        }

        var phoneKey = PhoneMatchKey(from.WhatsAppNumber) ?? PhoneMatchKey(from.Phone);

        if (match is null && phoneKey is not null)
            match = await MatchByPhoneAsync(phoneKey, ct);

        if (match is not null) return (match, false);

        var name = from.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = email ?? whatsapp ?? phone ?? "Unknown";

        var customer = new Customer
        {
            Code = await numbers.NextCustomerCodeAsync(ct),
            // The same name on both sides: we have one string and no way to translate it.
            // An agent completes the record when they pick the ticket up.
            FullNameAr = name,
            FullNameEn = name,
            Email = email,
            Phone = phone,
            WhatsAppNumber = whatsapp,
            PreferredLanguage = "ar",
            IsActive = true
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created customer {Code} from an inbound {Channel} message.", customer.Code, message.Channel);

        return (customer, true);
    }

    /// <summary>Compares on the trailing digits, which is why the candidate set is narrowed
    /// in SQL and the suffix compared in memory — the same normalisation cannot be expressed
    /// as a translatable predicate.</summary>
    private async Task<Customer?> MatchByPhoneAsync(string phoneKey, CancellationToken ct)
    {
        var suffix = phoneKey;

        var candidates = await db.Customers
            .Where(c => (c.Phone != null && c.Phone.Contains(suffix))
                        || (c.WhatsAppNumber != null && c.WhatsAppNumber.Contains(suffix)))
            .Take(20)
            .ToListAsync(ct);

        var byCustomer = candidates.FirstOrDefault(c =>
            PhoneMatchKey(c.Phone) == phoneKey || PhoneMatchKey(c.WhatsAppNumber) == phoneKey);

        if (byCustomer is not null) return byCustomer;

        var contacts = await db.CustomerContacts.AsNoTracking()
            .Where(c => c.Value.Contains(suffix))
            .Take(20)
            .ToListAsync(ct);

        var contact = contacts.FirstOrDefault(c => PhoneMatchKey(c.Value) == phoneKey);

        return contact is null
            ? null
            : await db.Customers.FirstOrDefaultAsync(c => c.Id == contact.CustomerId, ct);
    }

    private async Task<Customer?> MatchByContactAsync(string value, CancellationToken ct)
    {
        var contact = await db.CustomerContacts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Value == value, ct);

        return contact is null
            ? null
            : await db.Customers.FirstOrDefaultAsync(c => c.Id == contact.CustomerId, ct);
    }

    // ---- threading ----

    private async Task<Ticket?> FindThreadAsync(InboundMessage message, Customer customer, CancellationToken ct)
    {
        // A reference token in the subject is the strongest signal: we put it there.
        if (TicketReference.Extract(message.Subject) is { } number)
        {
            var referenced = await db.Tickets
                .FirstOrDefaultAsync(t => t.Number == number && t.CustomerId == customer.Id, ct);

            // Scoped to the customer on purpose: a token quoted by someone else must not
            // give them a way onto another customer's ticket.
            if (referenced is not null) return referenced;
        }

        // The provider's own conversation id, if we have seen it before.
        if (!string.IsNullOrWhiteSpace(message.ProviderConversationId))
        {
            var byConversation = await db.ChannelMessages.AsNoTracking()
                .Where(m => m.Channel == message.Channel
                            && m.ProviderConversationId == message.ProviderConversationId
                            && m.TicketId != null)
                .OrderByDescending(m => m.OccurredAt)
                .Select(m => m.TicketId)
                .FirstOrDefaultAsync(ct);

            if (byConversation is { } ticketId)
            {
                var found = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
                if (found is not null) return found;
            }
        }

        // An email reply quoting a message we sent.
        if (message.Headers is { Count: > 0 })
        {
            var references = new List<string>();

            foreach (var key in new[] { "In-Reply-To", "References" })
            {
                if (message.Headers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    references.AddRange(value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim('<', '>', ' ')));
                }
            }

            if (references.Count > 0)
            {
                var byHeader = await db.ChannelMessages.AsNoTracking()
                    .Where(m => m.ProviderMessageId != null
                                && references.Contains(m.ProviderMessageId)
                                && m.TicketId != null)
                    .OrderByDescending(m => m.OccurredAt)
                    .Select(m => m.TicketId)
                    .FirstOrDefaultAsync(ct);

                if (byHeader is { } headerTicketId)
                {
                    var found = await db.Tickets.FirstOrDefaultAsync(t => t.Id == headerTicketId, ct);
                    if (found is not null) return found;
                }
            }
        }

        // Nothing explicit. Fall back to the customer's most recent live ticket, but only
        // while it is recent — an unrelated question three months later is its own ticket.
        var cutoff = clock.UtcNow - RecentTicketWindow;
        var active = TicketWorkflow.ActiveStatuses;

        return await db.Tickets
            .Where(t => t.CustomerId == customer.Id
                        && active.Contains(t.Status)
                        && (t.ModifiedAt ?? t.CreatedAt) >= cutoff)
            .OrderByDescending(t => t.ModifiedAt ?? t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    // ---- attachments ----

    private async Task SaveAttachmentsAsync(InboundMessage message, Guid interactionId, CancellationToken ct)
    {
        if (message.Attachments is not { Count: > 0 }) return;

        foreach (var attachment in message.Attachments)
        {
            try
            {
                await using var content = attachment.OpenRead();

                var path = await storage.SaveAsync(
                    content, attachment.FileName, $"{AttachmentOwnerType.Interaction}/{interactionId:N}", ct);

                db.Attachments.Add(new Attachment
                {
                    OwnerType = AttachmentOwnerType.Interaction,
                    OwnerId = interactionId,
                    FileName = attachment.FileName,
                    ContentType = attachment.ContentType,
                    SizeBytes = attachment.SizeBytes,
                    StoragePath = path
                });
            }
            catch (Exception ex)
            {
                // A rejected attachment must not lose the message it came with. The text is
                // already saved; the failure is logged for an agent to chase.
                logger.LogWarning(ex,
                    "Could not store inbound attachment {FileName}; the message itself was kept.",
                    attachment.FileName);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    private static string BuildSubject(InboundMessage message)
    {
        var subject = message.Subject?.Trim();
        if (!string.IsNullOrWhiteSpace(subject)) return Truncate(subject, 300);

        // Channels like SMS carry no subject, so the first line of the body stands in.
        var firstLine = message.BodyText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();

        return string.IsNullOrWhiteSpace(firstLine)
            ? $"{message.Channel} message"
            : Truncate(firstLine, 120);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? AddressOf(InboundParty party) =>
        party.Email ?? party.WhatsAppNumber ?? party.Phone ?? party.ExternalUserId;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Canonical form for storage: digits only, international prefixes stripped,
    /// with a leading +.</summary>
    internal static string? NormalizePhone(string? value)
    {
        var digits = DigitsOf(value);
        return digits is null ? null : "+" + digits;
    }

    /// <summary>The trailing digits used for comparison.
    ///
    /// The same person writes their number as "+966 55 123 4567", "00966551234567" and
    /// "0551234567". Nothing in the message says which country a bare local number belongs
    /// to, so a prefix-based canonical form would leave those three as different customers.
    /// Comparing the last nine digits — the national significant number in Saudi Arabia and
    /// its neighbours — matches all three. It is deliberately a suffix match rather than a
    /// guessed country code: adding a prefix we do not know would be inventing data.</summary>
    internal const int PhoneMatchDigits = 9;

    internal static string? PhoneMatchKey(string? value)
    {
        var digits = DigitsOf(value);
        if (digits is null) return null;

        return digits.Length <= PhoneMatchDigits ? digits : digits[^PhoneMatchDigits..];
    }

    private static string? DigitsOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;

        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];

        return digits.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : digits;
    }
}
