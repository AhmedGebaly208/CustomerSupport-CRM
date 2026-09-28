namespace CustomerSupportCRM.Domain.Enums;

public enum WebhookDeliveryStatus
{
    Pending = 0,
    Delivered = 1,
    /// <summary>Failed but retriable; NextAttemptAt says when.</summary>
    Retrying = 2,
    /// <summary>Given up on. The subscription may also have been disabled.</summary>
    Failed = 3
}

/// <summary>Events an external system can subscribe to (PDF area 11).
///
/// Deliberately coarse. A receiver that needs to know a ticket changed does not need one
/// event per field, and a fine-grained stream would tie every future refactor of the desk to
/// somebody's integration.</summary>
public static class WebhookEvents
{
    public const string TicketCreated = "ticket.created";
    public const string TicketStatusChanged = "ticket.status_changed";
    public const string TicketAssigned = "ticket.assigned";
    public const string TicketResolved = "ticket.resolved";
    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";
    public const string SlaBreached = "sla.breached";

    public static readonly string[] All =
    [
        TicketCreated, TicketStatusChanged, TicketAssigned, TicketResolved,
        CustomerCreated, CustomerUpdated, SlaBreached
    ];
}
