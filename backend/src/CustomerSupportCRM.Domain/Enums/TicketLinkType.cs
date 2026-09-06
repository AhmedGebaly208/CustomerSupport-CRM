namespace CustomerSupportCRM.Domain.Enums;

/// <summary>How one ticket relates to another (PDF area 2).</summary>
public enum TicketLinkType
{
    /// <summary>The source is a duplicate of the target. Usually followed by a merge.</summary>
    DuplicateOf = 0,

    /// <summary>Loosely related; no ordering implied.</summary>
    RelatedTo = 1,

    /// <summary>The source blocks the target from being resolved.</summary>
    Blocks = 2
}
