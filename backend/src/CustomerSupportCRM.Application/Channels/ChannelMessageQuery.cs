using CustomerSupportCRM.Application.Channels.Dtos;
using CustomerSupportCRM.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Channels;

public interface IChannelMessageQuery
{
    Task<IReadOnlyList<ChannelMessageDto>> ForTicketAsync(Guid ticketId, CancellationToken ct = default);
}

/// <summary>Reads the delivery ledger. Split from the dispatcher so a read path does not
/// pull in the send machinery.</summary>
public sealed class ChannelMessageQuery(IAppDbContext db, IScopeProvider scope) : IChannelMessageQuery
{
    public async Task<IReadOnlyList<ChannelMessageDto>> ForTicketAsync(
        Guid ticketId, CancellationToken ct = default)
    {
        // Scoped through the ticket: the ledger repeats message bodies, so reaching it by a
        // guessed ticket id must not bypass department scoping.
        var ticket = await scope.Apply(db.Tickets.AsNoTracking())
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

        if (ticket is null) return [];

        return await db.ChannelMessages.AsNoTracking()
            .Where(m => m.TicketId == ticketId)
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => new ChannelMessageDto(
                m.Id, m.Channel, m.Direction, m.Address, m.Subject, m.BodyText,
                m.Status, m.AttemptCount, m.LastError, m.OccurredAt, m.NextAttemptAt,
                m.TicketCommentId))
            .ToListAsync(ct);
    }
}
