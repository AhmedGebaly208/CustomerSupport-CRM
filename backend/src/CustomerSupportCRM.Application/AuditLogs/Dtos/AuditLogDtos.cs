using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.AuditLogs.Dtos;

/// <summary>One audit-trail row (PDF area 10 "Audit logs").
///
/// <c>Changes</c> is the raw JSON map of changed property to { old, new } written by
/// <c>AuditingInterceptor</c>. It is passed through verbatim so the UI can render it
/// structurally; secrets were already redacted at write time.</summary>
public sealed record AuditLogDto(
    Guid Id,
    string EntityName,
    string EntityId,
    AuditAction Action,
    string? Changes,
    Guid? UserId,
    /// <summary>Resolved actor name: the account's current display name when it still
    /// exists, otherwise the name captured at write time.</summary>
    string? UserName,
    string? IpAddress,
    DateTimeOffset OccurredAt);

public sealed class AuditLogQuery : PagedQuery
{
    /// <summary>CLR entity name, e.g. "Customer" or "Ticket".</summary>
    public string? EntityName { get; set; }

    /// <summary>Primary key of the audited record, as written by the interceptor.</summary>
    public string? EntityId { get; set; }

    public AuditAction? Action { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset? DateFrom { get; set; }
    public DateTimeOffset? DateTo { get; set; }
}

/// <summary>Distinct entity names present in the trail, for the filter dropdown.</summary>
public sealed record AuditLogFacetsDto(IReadOnlyList<string> EntityNames);
