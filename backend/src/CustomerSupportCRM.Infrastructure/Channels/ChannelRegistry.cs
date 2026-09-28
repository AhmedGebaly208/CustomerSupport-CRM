using CustomerSupportCRM.Application.Channels;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Infrastructure.Channels;

/// <summary>Resolves the adapter for a channel, honouring the runtime toggles.
///
/// Two separate questions: whether an adapter exists at all (compile time — what this build
/// can speak) and whether the desk has turned it on (runtime — what it is speaking today).
/// A channel with no adapter cannot be enabled into working; a channel with one can be
/// switched off without a redeploy.</summary>
public sealed class ChannelRegistry(
    IEnumerable<IChannelAdapter> adapters,
    AppDbContext db,
    IConfigCache cache) : IChannelRegistry
{
    private readonly Dictionary<CommunicationChannel, IChannelAdapter> byChannel =
        adapters.GroupBy(a => a.Channel).ToDictionary(g => g.Key, g => g.Last());

    public IReadOnlyList<CommunicationChannel> Available => [.. byChannel.Keys.Order()];

    public async Task<bool> IsEnabledAsync(CommunicationChannel channel, CancellationToken ct = default)
    {
        if (!byChannel.ContainsKey(channel)) return false;

        var enabled = await EnabledChannelsAsync();
        return enabled.Contains(channel);
    }

    public async Task<IChannelAdapter?> ForAsync(CommunicationChannel channel, CancellationToken ct = default) =>
        await IsEnabledAsync(channel, ct) && byChannel.TryGetValue(channel, out var adapter)
            ? adapter
            : null;

    /// <summary>Cached because the dispatcher asks per message. SystemConfigService drops the
    /// key when a toggle changes, so switching a channel off takes effect on the next sweep.</summary>
    private Task<HashSet<CommunicationChannel>> EnabledChannelsAsync() =>
        cache.GetOrCreateAsync(ChannelCacheKeys.EnabledChannels, async () =>
        {
            var rows = await db.ChannelToggles.AsNoTracking()
                .Where(t => t.IsEnabled)
                .Select(t => t.Channel)
                .ToListAsync();

            return rows.ToHashSet();
        });
}
