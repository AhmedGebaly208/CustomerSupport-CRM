using System.Collections.Concurrent;
using CustomerSupportCRM.Application.Common.Interfaces;

namespace CustomerSupportCRM.Infrastructure.Caching;

/// <summary>In-process configuration cache.
///
/// Caches the <see cref="Task{T}"/> rather than the value, so concurrent first-time readers
/// share one database round-trip instead of stampeding. A failed factory is evicted so the
/// next caller retries rather than caching the failure forever.
///
/// Single-instance only: a multi-instance deployment needs a distributed cache or a
/// short expiry, since Remove here evicts on one node. Noted rather than solved, because
/// the deployment topology is not decided yet.</summary>
public sealed class MemoryConfigCache : IConfigCache
{
    private readonly ConcurrentDictionary<string, object> _entries = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        var task = (Task<T>)_entries.GetOrAdd(key, _ => factory());

        try
        {
            return await task;
        }
        catch
        {
            // Do not let a transient failure become a permanently cached error.
            _entries.TryRemove(key, out _);
            throw;
        }
    }

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();
}
