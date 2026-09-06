using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>System-wide audit trail (area 10). Written automatically by the persistence
/// interceptor for every tracked insert, update and soft-delete.
///
/// Append-only: rows are written solely by AuditingInterceptor. There is no
/// application-level create, update or delete path, and AuditLogsController exposes GET
/// verbs only — a write path would destroy the trail's value as evidence.
///
/// TODO(retention): this will become the largest table in the database. Move rows older
/// than an agreed number of months to a cold archive table on a schedule; not implemented
/// here because the retention period is a business decision, not a technical one.</summary>
public class AuditLog : BaseEntity
{
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }

    /// <summary>JSON map of changed property → { old, new }. Null for creates.</summary>
    public string? Changes { get; set; }

    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
