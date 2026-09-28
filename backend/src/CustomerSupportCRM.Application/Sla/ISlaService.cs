using CustomerSupportCRM.Application.Sla.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Sla;

namespace CustomerSupportCRM.Application.Sla;

/// <summary>Builds the desk's calendar from the configured business hours and holidays.
///
/// Separate from ISlaService because the calendar is read on nearly every SLA operation and
/// changes rarely, which makes it the natural place to cache.</summary>
public interface IBusinessCalendarProvider
{
    Task<BusinessCalendar> GetAsync(CancellationToken ct = default);
}

/// <summary>Cache keys shared between the code that fills them and the configuration service
/// that must drop them when the underlying rows change.</summary>
public static class SlaCacheKeys
{
    public const string BusinessCalendar = "config.businesscalendar";
}

/// <summary>Applies SLA policy to tickets: picks the policy, stamps the due dates, and
/// reports where a ticket stands.</summary>
public interface ISlaService
{
    /// <summary>The policy that best matches a ticket, or null when none does. Most specific
    /// match wins, with Rank breaking ties.</summary>
    Task<SlaPolicy?> ResolvePolicyAsync(Ticket ticket, CancellationToken ct = default);

    /// <summary>Stamps SlaPolicyId, FirstResponseDueAt and ResolutionDueAt on a ticket that
    /// is being created or whose priority, category or department changed.
    ///
    /// Mutates the entity but does not save — the caller owns the transaction.</summary>
    Task ApplyPolicyAsync(Ticket ticket, CancellationToken ct = default);

    Task<TicketSlaStatusDto> GetStatusAsync(Guid ticketId, CancellationToken ct = default);

    /// <summary>Statuses for many tickets at once, for the list view. Keyed by ticket id.</summary>
    Task<IReadOnlyDictionary<Guid, TicketSlaStatusDto>> GetStatusesAsync(
        IReadOnlyCollection<Guid> ticketIds, CancellationToken ct = default);

    Task<SlaPreviewDto> PreviewAsync(SlaPreviewRequest request, CancellationToken ct = default);
}

/// <summary>Picks an agent for a new ticket according to the policy's strategy.</summary>
public interface IAutoAssignmentService
{
    /// <summary>The agent to assign, or null when the policy assigns nothing or no agent is
    /// eligible. Returning null rather than throwing is deliberate: a ticket that cannot be
    /// auto-assigned must still be created and land in the unassigned queue.</summary>
    Task<Guid?> PickAgentAsync(Ticket ticket, SlaPolicy? policy, CancellationToken ct = default);
}

/// <summary>Raises in-app alerts. Implemented over the Notifications table; email and SMS
/// delivery hang off the same call once the channels story lands.</summary>
public interface INotificationService
{
    Task NotifyAsync(
        IReadOnlyCollection<Guid> userIds,
        NotificationRequest request,
        CancellationToken ct = default);

    Task<NotificationListDto> ListAsync(bool unreadOnly, int take, CancellationToken ct = default);

    Task MarkReadAsync(Guid id, CancellationToken ct = default);

    Task MarkAllReadAsync(CancellationToken ct = default);
}

/// <summary>What happened, and the values needed to describe it. No wording: the recipient's
/// language is not known when the event occurs, and may change afterwards.</summary>
public sealed record NotificationRequest(
    Domain.Enums.NotificationKind Kind,
    IReadOnlyDictionary<string, string>? Parameters = null,
    Guid? TicketId = null);

/// <summary>Sweeps tickets for approaching and missed targets. Driven by a hosted service in
/// the API, and exposed here so it can also be triggered on demand from an admin screen.</summary>
public interface ISlaEvaluator
{
    /// <summary>Evaluates every open ticket against its policy's rules and returns how many
    /// escalations fired.</summary>
    Task<int> EvaluateAsync(CancellationToken ct = default);
}
