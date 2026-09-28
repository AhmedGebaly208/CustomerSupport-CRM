using System.Security.Cryptography;
using System.Text;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Integrations;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Infrastructure.Integrations;

/// <summary>Delivers queued webhook events (PDF area 11).
///
/// Every request is signed with the subscription's secret, so a receiver can tell a genuine
/// delivery from anything else that found its endpoint. The signature covers a timestamp as
/// well as the body, which is what stops a captured delivery being replayed later.</summary>
public sealed class WebhookDispatcher(
    IAppDbContext db,
    IHttpClientFactory httpClientFactory,
    IClock clock,
    ILogger<WebhookDispatcher> logger) : IWebhookDispatcher
{
    /// <summary>Attempts before a delivery is parked.</summary>
    private const int MaxAttempts = 6;

    /// <summary>Consecutive failures before the subscription itself is switched off. A
    /// receiver that has been down this long is not coming back on its own, and continuing to
    /// queue for it fills the table.</summary>
    private const int FailuresBeforeDisable = 20;

    private const int BatchSize = 50;

    /// <summary>A receiver is not allowed to hold a connection open indefinitely; one slow
    /// endpoint would otherwise stall the whole sweep.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    public async Task<int> DispatchPendingAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var due = await db.WebhookDeliveries
            .Include(d => d.Subscription)
            .Where(d => (d.Status == WebhookDeliveryStatus.Pending
                         || d.Status == WebhookDeliveryStatus.Retrying)
                        && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .OrderBy(d => d.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return 0;

        var client = httpClientFactory.CreateClient("webhooks");
        client.Timeout = RequestTimeout;

        var delivered = 0;

        foreach (var delivery in due)
        {
            var subscription = delivery.Subscription;

            if (subscription is null || !subscription.IsActive)
            {
                delivery.Status = WebhookDeliveryStatus.Failed;
                delivery.LastError = "The subscription is inactive.";
                delivery.NextAttemptAt = null;
                continue;
            }

            delivery.AttemptCount++;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
                {
                    Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
                };

                var timestamp = now.ToUnixTimeSeconds().ToString();

                request.Headers.Add("X-CRM-Event", delivery.EventType);
                request.Headers.Add("X-CRM-Delivery", delivery.Id.ToString());
                request.Headers.Add("X-CRM-Timestamp", timestamp);
                request.Headers.Add("X-CRM-Signature", Sign(subscription.Secret, timestamp, delivery.Payload));

                using var response = await client.SendAsync(request, ct);

                delivery.LastStatusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    delivery.Status = WebhookDeliveryStatus.Delivered;
                    delivery.DeliveredAt = now;
                    delivery.NextAttemptAt = null;
                    delivery.LastError = null;

                    subscription.ConsecutiveFailures = 0;
                    subscription.LastDeliveryAt = now;
                    delivered++;
                    continue;
                }

                // 4xx other than 408 and 429 means the receiver understood and refused;
                // retrying will not change its mind.
                var retriable = (int)response.StatusCode >= 500
                                || (int)response.StatusCode is 408 or 429;

                Fail(delivery, subscription, $"HTTP {(int)response.StatusCode}", retriable, now);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                Fail(delivery, subscription, "The request timed out.", retriable: true, now);
            }
            catch (HttpRequestException ex)
            {
                Fail(delivery, subscription, ex.Message, retriable: true, now);
            }
        }

        await db.SaveChangesAsync(ct);

        if (delivered > 0)
            logger.LogInformation("Delivered {Count} webhook event(s).", delivered);

        return delivered;
    }

    private void Fail(
        Domain.Entities.WebhookDelivery delivery,
        Domain.Entities.WebhookSubscription subscription,
        string error,
        bool retriable,
        DateTimeOffset now)
    {
        delivery.LastError = error;
        subscription.ConsecutiveFailures++;

        if (retriable && delivery.AttemptCount < MaxAttempts)
        {
            delivery.Status = WebhookDeliveryStatus.Retrying;
            // Exponential backoff: a receiver that is rate limiting or restarting wants
            // space, and a tight loop makes an outage worse.
            delivery.NextAttemptAt = now.AddSeconds(Math.Pow(3, delivery.AttemptCount) * 5);
        }
        else
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            delivery.NextAttemptAt = null;
        }

        if (subscription.ConsecutiveFailures >= FailuresBeforeDisable && subscription.IsActive)
        {
            subscription.IsActive = false;
            subscription.DisabledAt = now;

            logger.LogWarning(
                "Disabled webhook subscription {Name} after {Count} consecutive failures.",
                subscription.Name, subscription.ConsecutiveFailures);
        }
    }

    /// <summary>HMAC-SHA256 over "timestamp.payload".
    ///
    /// The timestamp is inside the signed string, not merely sent alongside it — otherwise a
    /// captured delivery could be replayed with a fresh header and still verify.</summary>
    private static string Sign(string secret, string timestamp, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes($"{timestamp}.{payload}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes);

        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
