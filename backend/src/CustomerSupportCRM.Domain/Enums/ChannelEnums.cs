namespace CustomerSupportCRM.Domain.Enums;

public enum ChannelDirection
{
    Inbound = 0,
    Outbound = 1
}

/// <summary>Where a channel message stands. Inbound messages are Delivered on arrival —
/// they are already here — so the states only really move for outbound.</summary>
public enum ChannelMessageStatus
{
    /// <summary>Queued, not yet handed to the provider.</summary>
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    /// <summary>Failed but retriable; NextAttemptAt says when.</summary>
    Retrying = 3,
    /// <summary>Given up on. An agent has to do something about it.</summary>
    Failed = 4
}
