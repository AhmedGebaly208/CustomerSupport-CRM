using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>A customer's rating of how their ticket was handled (PDF area 8, CSAT).
///
/// One per ticket, so a customer cannot be asked twice and a second submission corrects the
/// first rather than skewing the average. Stored against the ticket rather than the agent
/// because the customer is rating the outcome, not a person — which agent gets the credit is
/// a reporting decision made later, from the assignment history.</summary>
public class TicketSatisfaction : BaseEntity
{
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    /// <summary>1 to 5. A five-point scale because it is what customers are used to, and
    /// because a mean over three points says almost nothing.</summary>
    public int Score { get; set; }

    public string? Comment { get; set; }

    /// <summary>Who rated. Null for a survey answered from an emailed link, which is the
    /// common case — requiring a sign-in to rate would collapse the response rate.</summary>
    public Guid? RespondentUserId { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
}
