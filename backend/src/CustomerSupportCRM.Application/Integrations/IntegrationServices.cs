using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Integrations;

// ---- DTOs ----

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    int RateLimitPerMinute,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt);

/// <summary>The one and only time the plain key is returned. It is not stored, so it cannot
/// be shown again — the caller has to save it now.</summary>
public sealed record CreatedApiKeyDto(ApiKeyDto Key, string PlainKey);

public sealed record SaveApiKeyRequest(
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    int RateLimitPerMinute);

public sealed record WebhookSubscriptionDto(
    Guid Id,
    string Name,
    string Url,
    IReadOnlyList<string> Events,
    bool IsActive,
    int ConsecutiveFailures,
    DateTimeOffset? LastDeliveryAt,
    DateTimeOffset? DisabledAt);

public sealed record CreatedWebhookDto(WebhookSubscriptionDto Subscription, string Secret);

public sealed record SaveWebhookRequest(
    string Name,
    string Url,
    IReadOnlyList<string> Events,
    bool IsActive);

public sealed record WebhookDeliveryDto(
    Guid Id,
    string EventType,
    WebhookDeliveryStatus Status,
    int AttemptCount,
    int? LastStatusCode,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? NextAttemptAt);

// ---- Contracts ----

/// <summary>The authenticated API key behind the current request, when there is one.</summary>
public interface IApiKeyContext
{
    Guid? KeyId { get; }
    IReadOnlySet<string> Scopes { get; }
    bool IsApiKeyRequest { get; }
}

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct = default);
    Task<CreatedApiKeyDto> CreateAsync(SaveApiKeyRequest request, CancellationToken ct = default);
    Task RevokeAsync(Guid id, string? reason, CancellationToken ct = default);

    /// <summary>Validates a presented key and returns it, or null. Used by the authentication
    /// handler and nothing else.</summary>
    Task<ApiKey?> AuthenticateAsync(string presentedKey, CancellationToken ct = default);
}

public interface IWebhookService
{
    Task<IReadOnlyList<WebhookSubscriptionDto>> ListAsync(CancellationToken ct = default);
    Task<CreatedWebhookDto> CreateAsync(SaveWebhookRequest request, CancellationToken ct = default);
    Task<WebhookSubscriptionDto> UpdateAsync(Guid id, SaveWebhookRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookDeliveryDto>> ListDeliveriesAsync(Guid id, CancellationToken ct = default);
    Task RedeliverAsync(Guid subscriptionId, Guid deliveryId, CancellationToken ct = default);
}

/// <summary>Raises an event to every subscription that asked for it. Called from the services
/// that change things; they do not know who is listening.</summary>
public interface IWebhookPublisher
{
    Task PublishAsync(string eventType, object payload, CancellationToken ct = default);
}

/// <summary>Sends queued deliveries and retries the ones that failed.</summary>
public interface IWebhookDispatcher
{
    Task<int> DispatchPendingAsync(CancellationToken ct = default);
}

// ---- Implementations ----

/// <summary>API keys for external systems (PDF area 11).
///
/// A key is `csk_` plus a prefix plus a secret. The prefix is stored in clear so an operator
/// can identify a key in a list or a log; the secret is stored only as a hash, so a leaked
/// database backup does not hand over working credentials.</summary>
public sealed class ApiKeyService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IApiKeyService
{
    private const string KeyPrefix = "csk";
    private const int PrefixLength = 8;

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.ApiKeys.AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    public async Task<CreatedApiKeyDto> CreateAsync(
        SaveApiKeyRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BadRequestException("An API key needs a name.");

        if (request.Scopes.Count == 0)
            throw new BadRequestException("An API key needs at least one scope.");

        // Only permissions that exist. A typo would otherwise create a key that silently
        // grants nothing, and the operator would not find out until the integration failed.
        var unknown = request.Scopes.Where(s => !Auth.Permissions.All.Contains(s)).ToList();

        if (unknown.Count > 0)
            throw new BadRequestException($"Unknown scope(s): {string.Join(", ", unknown)}.");

        var prefix = RandomToken(PrefixLength);
        var secret = RandomToken(32);
        var plain = $"{KeyPrefix}_{prefix}_{secret}";

        var key = new ApiKey
        {
            Name = request.Name.Trim(),
            Prefix = prefix,
            KeyHash = Hash(secret),
            Scopes = string.Join(',', request.Scopes.Distinct()),
            ExpiresAt = request.ExpiresAt,
            RateLimitPerMinute = Math.Clamp(request.RateLimitPerMinute, 1, 10_000),
            IsActive = true
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return new CreatedApiKeyDto(ToDto(key), plain);
    }

    public async Task RevokeAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct)
            ?? throw new NotFoundException(nameof(ApiKey), id);

        // Revoked rather than deleted: the audit trail references it, and an operator asking
        // "what was this key and who turned it off" deserves an answer.
        key.IsActive = false;
        key.RevokedAt = clock.UtcNow;
        key.RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await db.SaveChangesAsync(ct);
    }

    public async Task<ApiKey?> AuthenticateAsync(string presentedKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(presentedKey)) return null;

        var parts = presentedKey.Split('_');
        if (parts.Length != 3 || parts[0] != KeyPrefix) return null;

        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == parts[1], ct);
        if (key is null || !key.IsActive) return null;

        if (key.ExpiresAt is { } expiry && expiry <= clock.UtcNow) return null;

        // Fixed-time comparison: a length-sensitive or early-exit compare leaks how much of a
        // guess was right, which is enough to recover a key one byte at a time.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Hash(parts[2])),
                Encoding.UTF8.GetBytes(key.KeyHash)))
        {
            return null;
        }

        // Written without a full save cycle so a busy integration does not turn every read
        // into a write of the whole entity.
        await db.ApiKeys
            .Where(k => k.Id == key.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, clock.UtcNow), ct);

        return key;
    }

    private static string RandomToken(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static ApiKeyDto ToDto(ApiKey k) => new(
        k.Id, k.Name, k.Prefix,
        k.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        k.IsActive, k.ExpiresAt, k.LastUsedAt, k.RateLimitPerMinute, k.RevokedAt, k.CreatedAt);
}

/// <summary>Webhook subscriptions and their delivery history (PDF area 11).</summary>
public sealed class WebhookService(
    IAppDbContext db,
    IClock clock,
    ICurrentUser currentUser) : IWebhookService, IWebhookPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WebhookSubscriptionDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.WebhookSubscriptions.AsNoTracking()
            .OrderBy(w => w.Name)
            .ToListAsync(ct);

        return rows.Select(ToDto).ToList();
    }

    public async Task<CreatedWebhookDto> CreateAsync(
        SaveWebhookRequest request, CancellationToken ct = default)
    {
        Guard(request);

        var subscription = new WebhookSubscription
        {
            Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()
        };

        Apply(subscription, request);

        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        // The secret is returned once, at creation. It stays readable in the database because
        // signing needs the value itself, but it is never returned again.
        return new CreatedWebhookDto(ToDto(subscription), subscription.Secret);
    }

    public async Task<WebhookSubscriptionDto> UpdateAsync(
        Guid id, SaveWebhookRequest request, CancellationToken ct = default)
    {
        Guard(request);

        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException(nameof(WebhookSubscription), id);

        Apply(subscription, request);

        // Re-enabling clears the failure count, otherwise a subscription fixed by its owner
        // would be disabled again by the first hiccup.
        if (request.IsActive && subscription.DisabledAt is not null)
        {
            subscription.ConsecutiveFailures = 0;
            subscription.DisabledAt = null;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(subscription);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var subscription = await db.WebhookSubscriptions.FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new NotFoundException(nameof(WebhookSubscription), id);

        subscription.IsDeleted = true;
        subscription.DeletedAt = clock.UtcNow;
        subscription.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<WebhookDeliveryDto>> ListDeliveriesAsync(
        Guid id, CancellationToken ct = default)
    {
        return await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.SubscriptionId == id)
            .OrderByDescending(d => d.CreatedAt)
            .Take(100)
            .Select(d => new WebhookDeliveryDto(
                d.Id, d.EventType, d.Status, d.AttemptCount, d.LastStatusCode,
                d.LastError, d.CreatedAt, d.DeliveredAt, d.NextAttemptAt))
            .ToListAsync(ct);
    }

    public async Task RedeliverAsync(Guid subscriptionId, Guid deliveryId, CancellationToken ct = default)
    {
        var delivery = await db.WebhookDeliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId && d.SubscriptionId == subscriptionId, ct)
            ?? throw new NotFoundException(nameof(WebhookDelivery), deliveryId);

        // The stored payload is resent unchanged. Regenerating it would describe the state as
        // it is now, which is not the event that happened.
        delivery.Status = WebhookDeliveryStatus.Pending;
        delivery.AttemptCount = 0;
        delivery.NextAttemptAt = clock.UtcNow;
        delivery.LastError = null;

        await db.SaveChangesAsync(ct);
    }

    public async Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
    {
        var subscriptions = await db.WebhookSubscriptions.AsNoTracking()
            .Where(w => w.IsActive)
            .ToListAsync(ct);

        var interested = subscriptions
            .Where(w => w.Events
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(eventType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (interested.Count == 0) return;

        var now = clock.UtcNow;

        var body = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            type = eventType,
            occurredAt = now,
            data = payload
        }, Json);

        foreach (var subscription in interested)
        {
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                SubscriptionId = subscription.Id,
                EventType = eventType,
                Payload = body,
                Status = WebhookDeliveryStatus.Pending,
                CreatedAt = now,
                NextAttemptAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static void Guard(SaveWebhookRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new BadRequestException("A webhook needs a name.");

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BadRequestException("The webhook URL must be an absolute http or https address.");
        }

        if (request.Events.Count == 0)
            throw new BadRequestException("A webhook needs at least one event.");

        var unknown = request.Events.Where(e => !WebhookEvents.All.Contains(e)).ToList();

        if (unknown.Count > 0)
            throw new BadRequestException($"Unknown event(s): {string.Join(", ", unknown)}.");
    }

    private static void Apply(WebhookSubscription subscription, SaveWebhookRequest request)
    {
        subscription.Name = request.Name.Trim();
        subscription.Url = request.Url.Trim();
        subscription.Events = string.Join(',', request.Events.Distinct());
        subscription.IsActive = request.IsActive;
    }

    private static WebhookSubscriptionDto ToDto(WebhookSubscription w) => new(
        w.Id, w.Name, w.Url,
        w.Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        w.IsActive, w.ConsecutiveFailures, w.LastDeliveryAt, w.DisabledAt);
}
