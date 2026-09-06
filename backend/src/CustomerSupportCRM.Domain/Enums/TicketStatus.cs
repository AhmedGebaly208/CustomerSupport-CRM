namespace CustomerSupportCRM.Domain.Enums;

/// <summary>Ticket lifecycle. Allowed transitions live in
/// <see cref="CustomerSupportCRM.Domain.Tickets.TicketWorkflow"/>.</summary>
public enum TicketStatus
{
    New = 0,
    Open = 1,
    Pending = 2,
    OnHold = 3,
    Resolved = 4,
    Closed = 5,
    Reopened = 6
}
