using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Channels;

/// <summary>Cache keys shared between the code that fills them and the configuration
/// service that drops them when the underlying rows change.</summary>
public static class ChannelCacheKeys
{
    public const string EnabledChannels = "config.channeltoggles";
}

// ---- Inbound ----

/// <summary>Someone on the far side of a channel. Which field is filled depends on the
/// channel, which is why they are all optional: an email carries an address, a WhatsApp
/// message a number, a live chat only a handle.</summary>
public sealed record InboundParty(
    string? DisplayName = null,
    string? Email = null,
    string? Phone = null,
    string? WhatsAppNumber = null,
    string? ExternalUserId = null);

public sealed record InboundAttachment(
    string FileName,
    string ContentType,
    long SizeBytes,
    Func<Stream> OpenRead);

/// <summary>A message that arrived from outside, already parsed out of whatever shape the
/// provider used. Adapters translate into this; nothing downstream knows about providers.</summary>
public sealed record InboundMessage(
    CommunicationChannel Channel,
    string ProviderMessageId,
    InboundParty From,
    string BodyText,
    DateTimeOffset ReceivedAt,
    string? ProviderConversationId = null,
    string? Subject = null,
    IReadOnlyList<InboundAttachment>? Attachments = null,
    /// <summary>Provider headers that help threading — In-Reply-To and References for
    /// email, for instance.</summary>
    IReadOnlyDictionary<string, string>? Headers = null);

// ---- Outbound ----

public sealed record OutboundParty(
    string? DisplayName = null,
    string? Email = null,
    string? Phone = null,
    string? WhatsAppNumber = null,
    string? ExternalUserId = null);

public sealed record OutboundAttachment(string FileName, string ContentType, Func<Stream> OpenRead);

public sealed record OutboundMessage(
    CommunicationChannel Channel,
    Guid TicketId,
    string TicketNumber,
    OutboundParty To,
    string BodyText,
    string? Subject = null,
    string? ProviderConversationId = null,
    IReadOnlyList<OutboundAttachment>? Attachments = null);

/// <summary>What happened when an adapter tried to send.
///
/// ShouldRetry is the adapter's judgement, not the dispatcher's: only the adapter knows
/// whether a provider's error was a rate limit worth waiting out or a rejected address that
/// will never succeed.</summary>
public sealed record ChannelSendResult(
    bool Success,
    string? ProviderMessageId = null,
    string? Error = null,
    bool ShouldRetry = false)
{
    public static ChannelSendResult Ok(string? providerMessageId = null) => new(true, providerMessageId);

    public static ChannelSendResult Transient(string error) => new(false, null, error, ShouldRetry: true);

    public static ChannelSendResult Permanent(string error) => new(false, null, error, ShouldRetry: false);
}

/// <summary>One provider binding. Implementations translate a provider's wire format into
/// InboundMessage and back out of OutboundMessage, and know nothing about tickets.</summary>
public interface IChannelAdapter
{
    CommunicationChannel Channel { get; }

    Task<ChannelSendResult> SendAsync(OutboundMessage message, CancellationToken ct = default);
}

/// <summary>Finds the adapter for a channel, honouring the runtime toggles so a channel can
/// be switched off without a redeploy.</summary>
public interface IChannelRegistry
{
    Task<bool> IsEnabledAsync(CommunicationChannel channel, CancellationToken ct = default);

    /// <summary>The adapter for a channel, or null when none is registered or the channel is
    /// switched off. Null rather than throwing: an agent replying on a disabled channel
    /// should get a clear message, not a 500.</summary>
    Task<IChannelAdapter?> ForAsync(CommunicationChannel channel, CancellationToken ct = default);

    /// <summary>Channels that have an adapter compiled in, whatever their toggle says. Used
    /// by the admin screen to show which channels can be turned on at all.</summary>
    IReadOnlyList<CommunicationChannel> Available { get; }
}

/// <summary>Turns an arriving message into a ticket, threading it onto an existing one when
/// it belongs there.</summary>
public interface IInboundMessageProcessor
{
    Task<InboundResult> ProcessAsync(InboundMessage message, CancellationToken ct = default);
}

public sealed record InboundResult(
    Guid TicketId,
    string TicketNumber,
    Guid CustomerId,
    bool CreatedTicket,
    bool CreatedCustomer,
    /// <summary>True when the provider had already delivered this message and nothing was
    /// done a second time.</summary>
    bool Duplicate);

/// <summary>Sends agent replies out on the channel the customer used, and retries the ones
/// that fail transiently.</summary>
public interface IOutboundDispatcher
{
    /// <summary>Queues a reply for delivery. Called after a public comment is saved.</summary>
    Task QueueAsync(Guid ticketId, Guid commentId, CancellationToken ct = default);

    /// <summary>Attempts every message that is pending or due for retry, and returns how
    /// many were delivered.</summary>
    Task<int> DispatchPendingAsync(CancellationToken ct = default);
}
