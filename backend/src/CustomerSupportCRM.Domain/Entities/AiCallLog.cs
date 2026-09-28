using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>One call to the assistance provider (PDF area 7).
///
/// Recorded for two reasons that pull in the same direction: cost, because tokens are
/// billed, and accountability, because a suggestion that reached a customer should be
/// traceable to what produced it.</summary>
public class AiCallLog : BaseEntity
{
    public AiFeature Feature { get; set; }
    public AiCallOutcome Outcome { get; set; }

    public string? Provider { get; set; }
    public string? Model { get; set; }

    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int LatencyMs { get; set; }

    /// <summary>The prompt and completion are deliberately not stored. They contain customer
    /// text, and keeping a second copy outside the ticket would widen the blast radius of a
    /// breach for no operational gain. The counts and the outcome answer the questions this
    /// table exists for.</summary>
    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid? UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public string? Error { get; set; }
}

/// <summary>What an agent did with a suggestion they were shown.</summary>
public class AiSuggestionFeedback : BaseEntity
{
    public AiFeature Feature { get; set; }
    public AiSuggestionOutcome Outcome { get; set; }

    public Guid? TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid? UserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
