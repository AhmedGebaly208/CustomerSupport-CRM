using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Application.Channels;

/// <summary>Sends agent replies back out on the channel the customer used (PDF area 3).
///
/// Queue first, send later. A reply is written to the ledger inside the same request that
/// saved the comment, and delivered by a sweep afterwards — so a provider being slow or down
/// never makes an agent wait, and never loses their reply.</summary>
public sealed class OutboundDispatcher(
    IAppDbContext db,
    IChannelRegistry registry,
    IClock clock,
    ILogger<OutboundDispatcher> logger) : IOutboundDispatcher
{
    /// <summary>Attempts before a message is parked as Failed for a human to deal with.</summary>
    private const int MaxAttempts = 5;

    private const int BatchSize = 50;

    public async Task QueueAsync(Guid ticketId, Guid commentId, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .Include(t => t.Customer)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

        if (ticket?.Customer is null) return;

        // Internal-only channels have no outside to send to.
        if (ticket.Channel is CommunicationChannel.Internal or CommunicationChannel.Portal
            or CommunicationChannel.WebForm or CommunicationChannel.Phone)
        {
            return;
        }

        if (!await registry.IsEnabledAsync(ticket.Channel, ct))
        {
            logger.LogInformation(
                "Ticket {TicketNumber} is on {Channel}, which is not enabled; the reply stays in the CRM only.",
                ticket.Number, ticket.Channel);
            return;
        }

        var comment = await db.TicketComments.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == commentId, ct);

        if (comment is null || comment.IsInternal) return;

        // One delivery chain per comment. A retried request must not queue a second copy.
        if (await db.ChannelMessages.AnyAsync(m => m.TicketCommentId == commentId, ct)) return;

        db.ChannelMessages.Add(new ChannelMessage
        {
            Channel = ticket.Channel,
            Direction = ChannelDirection.Outbound,
            Address = AddressFor(ticket.Channel, ticket.Customer),
            Subject = TicketReference.Stamp(ticket.Number, ticket.Subject),
            BodyText = comment.Body,
            Status = ChannelMessageStatus.Pending,
            OccurredAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow,
            TicketId = ticket.Id,
            CustomerId = ticket.CustomerId,
            TicketCommentId = commentId
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task<int> DispatchPendingAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var due = await db.ChannelMessages
            .Include(m => m.Ticket)
            .Where(m => m.Direction == ChannelDirection.Outbound
                        && (m.Status == ChannelMessageStatus.Pending
                            || m.Status == ChannelMessageStatus.Retrying)
                        && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        var delivered = 0;

        foreach (var message in due)
        {
            var adapter = await registry.ForAsync(message.Channel, ct);

            if (adapter is null)
            {
                // The channel was switched off after the reply was queued. Parked rather
                // than retried forever, so it shows up as needing attention.
                Park(message, "No adapter is registered or the channel is disabled.", now);
                continue;
            }

            message.AttemptCount++;

            ChannelSendResult result;

            try
            {
                result = await adapter.SendAsync(BuildOutbound(message), ct);
            }
            catch (Exception ex)
            {
                // An adapter that throws is treated as a transient failure: the sweep will
                // try again, and a genuinely permanent fault will exhaust the attempts.
                logger.LogError(ex, "Adapter for {Channel} threw while sending.", message.Channel);
                result = ChannelSendResult.Transient(ex.Message);
            }

            if (result.Success)
            {
                message.Status = ChannelMessageStatus.Sent;
                message.ProviderMessageId = result.ProviderMessageId;
                message.LastError = null;
                message.NextAttemptAt = null;
                delivered++;
            }
            else if (result.ShouldRetry && message.AttemptCount < MaxAttempts)
            {
                message.Status = ChannelMessageStatus.Retrying;
                message.LastError = result.Error;
                // Exponential backoff: a provider that is rate limiting wants space, and a
                // tight retry loop would make an outage worse.
                message.NextAttemptAt = now.AddSeconds(Math.Pow(2, message.AttemptCount) * 30);
            }
            else
            {
                Park(message, result.Error ?? "Delivery failed.", now);
            }
        }

        await db.SaveChangesAsync(ct);

        if (delivered > 0)
            logger.LogInformation("Delivered {Count} outbound message(s).", delivered);

        return delivered;
    }

    private static void Park(ChannelMessage message, string error, DateTimeOffset now)
    {
        message.Status = ChannelMessageStatus.Failed;
        message.LastError = error;
        message.NextAttemptAt = null;
        message.OccurredAt = message.OccurredAt == default ? now : message.OccurredAt;
    }

    private static OutboundMessage BuildOutbound(ChannelMessage message) => new(
        message.Channel,
        message.TicketId ?? Guid.Empty,
        message.Ticket?.Number ?? string.Empty,
        new OutboundParty(
            Email: message.Channel == CommunicationChannel.Email ? message.Address : null,
            Phone: message.Channel == CommunicationChannel.Sms ? message.Address : null,
            WhatsAppNumber: message.Channel == CommunicationChannel.WhatsApp ? message.Address : null,
            ExternalUserId: message.Channel == CommunicationChannel.LiveChat ? message.Address : null),
        message.BodyText,
        message.Subject,
        message.ProviderConversationId);

    private static string? AddressFor(CommunicationChannel channel, Customer customer) => channel switch
    {
        CommunicationChannel.Email => customer.Email,
        CommunicationChannel.WhatsApp => customer.WhatsAppNumber ?? customer.Phone,
        CommunicationChannel.Sms => customer.Phone,
        _ => customer.Email ?? customer.Phone
    };
}
