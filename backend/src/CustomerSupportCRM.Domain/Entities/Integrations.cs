using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>A credential an external system authenticates with (PDF area 11).
///
/// Only a hash is stored, exactly as for a user password. A key that can be read back out of
/// the database is a key that leaks with a backup, and the only moment anyone needs the plain
/// value is the moment it is created.</summary>
public class ApiKey : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The first characters of the key, kept in clear so an operator can tell two
    /// keys apart in a list and match one to a log line without ever seeing the secret.</summary>
    public string Prefix { get; set; } = string.Empty;

    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Comma-separated permission names. A key is deliberately not a user and holds
    /// no role: an integration should be able to read tickets without inheriting everything
    /// an agent can do.</summary>
    public string Scopes { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Null never expires. Expiry is offered because a key handed to a contractor
    /// should stop working on its own rather than relying on someone remembering.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Requests per minute this key may make. Per key rather than global so one
    /// noisy integration cannot starve the others.</summary>
    public int RateLimitPerMinute { get; set; } = 120;

    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
}

/// <summary>Where to deliver events, and what to deliver (PDF area 11).</summary>
public class WebhookSubscription : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>Comma-separated event names. A subscription that wanted everything would make
    /// the receiver filter, and most receivers care about one or two things.</summary>
    public string Events { get; set; } = string.Empty;

    /// <summary>Shared secret the delivery is signed with. Held in clear because signing
    /// needs the value itself, unlike an API key which is only ever compared.</summary>
    public string Secret { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Consecutive failures. A receiver that has been down for a long time is
    /// disabled rather than retried forever.</summary>
    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? LastDeliveryAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
}

/// <summary>One event queued for delivery to one subscription.
///
/// A row per subscription rather than per event, so a slow receiver cannot hold up a fast
/// one and a redelivery targets exactly what failed.</summary>
public class WebhookDelivery : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public WebhookSubscription? Subscription { get; set; }

    public string EventType { get; set; } = string.Empty;

    /// <summary>The event body as it will be sent. Stored so a redelivery sends exactly what
    /// the first attempt did — regenerating it could describe a state that has since moved
    /// on, which would make the receiver's view of history wrong.</summary>
    public string Payload { get; set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
