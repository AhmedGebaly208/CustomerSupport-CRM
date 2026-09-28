using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.Channels.Dtos;

/// <summary>Whether a channel can be spoken at all, and whether it currently is.</summary>
public sealed record ChannelStatusDto(
    CommunicationChannel Channel,
    /// <summary>An adapter is compiled into this build.</summary>
    bool HasAdapter,
    bool IsEnabled);

public sealed record ChannelMessageDto(
    Guid Id,
    CommunicationChannel Channel,
    ChannelDirection Direction,
    string? Address,
    string? Subject,
    string BodyText,
    ChannelMessageStatus Status,
    int AttemptCount,
    string? LastError,
    DateTimeOffset OccurredAt,
    DateTimeOffset? NextAttemptAt,
    Guid? TicketCommentId);

/// <summary>A message handed in over the API. Shaped for a provider webhook to be mapped
/// onto, and used directly by the local drop adapter's inbox.</summary>
public sealed record InboundWebhookRequest(
    CommunicationChannel Channel,
    string Body,
    string? ProviderMessageId = null,
    string? ConversationId = null,
    string? Subject = null,
    string? FromName = null,
    string? FromEmail = null,
    string? FromPhone = null,
    string? FromWhatsApp = null,
    string? FromExternalId = null,
    DateTimeOffset? ReceivedAt = null,
    IReadOnlyDictionary<string, string>? Headers = null);
