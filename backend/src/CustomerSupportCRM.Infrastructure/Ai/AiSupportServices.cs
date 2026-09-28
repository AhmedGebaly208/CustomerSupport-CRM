using CustomerSupportCRM.Application.Ai;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CustomerSupportCRM.Infrastructure.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Calls one user may make per feature per window.</summary>
    public int RequestsPerWindow { get; set; } = 20;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Features switched off entirely. Empty means all five are available.</summary>
    public AiFeature[] DisabledFeatures { get; set; } = [];
}

/// <summary>A fixed-window counter held in memory.
///
/// In memory on purpose: the cap protects a process from one agent holding down a button,
/// and a distributed limiter would need shared state this deployment does not have. Across
/// several instances each enforces its own share — a known looseness, acceptable because
/// this is a cost guard and not a security control.</summary>
public sealed class AiRateLimiter(IMemoryCache cache, IOptions<AiOptions> options) : IAiRateLimiter
{
    private readonly AiOptions options = options.Value;

    public Task<bool> TryAcquireAsync(AiFeature feature, string scope, CancellationToken ct = default)
    {
        if (options.DisabledFeatures.Contains(feature)) return Task.FromResult(false);

        var key = $"ai:{feature}:{scope}";

        var count = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = options.Window;
            return 0;
        });

        if (count >= options.RequestsPerWindow) return Task.FromResult(false);

        // The window is fixed, not sliding: the replacement entry keeps a fresh absolute
        // expiry from now, which is close enough for a cost guard and far simpler than
        // tracking the original start.
        cache.Set(key, count + 1, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = options.Window
        });

        return Task.FromResult(true);
    }
}

/// <summary>Writes the cost and audit row for every call.</summary>
public sealed class AiCallLogger(
    AppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    IAiCompletionService completions) : IAiCallLogger
{
    public async Task LogAsync(
        AiFeature feature, AiCompletionResult result, Guid? ticketId, CancellationToken ct = default)
    {
        db.AiCallLogs.Add(new AiCallLog
        {
            Feature = feature,
            Outcome = result.Outcome,
            Provider = completions.Name,
            Model = result.Model,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            LatencyMs = result.LatencyMs,
            TicketId = ticketId,
            UserId = currentUser.UserId,
            OccurredAt = clock.UtcNow,
            // The rationale explains the outcome and carries no customer text, so it is safe
            // to keep. The prompt and the completion deliberately are not stored.
            Error = result.Outcome is AiCallOutcome.Failed or AiCallOutcome.NoAnswer
                ? Truncate(result.Rationale, 1000)
                : null
        });

        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
