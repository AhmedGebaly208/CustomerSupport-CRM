using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>One message that crossed a channel boundary, inbound or outbound (PDF area 3).
///
/// This is the ledger that makes channels trustworthy. It gives inbound processing an
/// idempotency key so a provider retrying a webhook cannot raise the same ticket twice, and
/// it gives outbound a delivery record so an agent can see whether their reply actually
/// left the building.</summary>
public class ChannelMessage : BaseEntity
{
    public CommunicationChannel Channel { get; set; }
    public ChannelDirection Direction { get; set; }

    /// <summary>The provider's own id for this message. Unique per channel, and the reason a
    /// replayed webhook is a no-op rather than a duplicate ticket.</summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>The provider's thread or conversation id, where it has one. Used to thread a
    /// reply back onto the same ticket when no reference token survived.</summary>
    public string? ProviderConversationId { get; set; }

    /// <summary>Who it came from or went to, as the channel expresses it — an address, a
    /// phone number, a chat handle.</summary>
    public string? Address { get; set; }

    public string? Subject { get; set; }
    public string BodyText { get; set; } = string.Empty;

    public ChannelMessageStatus Status { get; set; } = ChannelMessageStatus.Pending;

    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid? InteractionId { get; set; }

    /// <summary>The comment this message was sent for. Our own idempotency key on the
    /// outbound side: one delivery attempt chain per reply.</summary>
    public Guid? TicketCommentId { get; set; }
}
