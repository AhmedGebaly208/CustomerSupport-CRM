namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Cache for runtime configuration that is read far more often than it is written —
/// branding on every SPA boot, business hours on every SLA evaluation.
///
/// Invalidation is explicit rather than time-based: a configuration change must take effect
/// immediately, and an administrator who flips a feature flag should not have to wait for an
/// expiry to see it.</summary>
public interface IConfigCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory);

    void Remove(string key);

    void Clear();
}
