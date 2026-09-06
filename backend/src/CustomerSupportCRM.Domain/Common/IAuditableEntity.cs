namespace CustomerSupportCRM.Domain.Common;

/// <summary>Populated centrally by the persistence interceptor, never by hand in services.
/// Backs the "Audit logs" requirement (area 10).</summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    DateTimeOffset? ModifiedAt { get; set; }
    Guid? ModifiedBy { get; set; }
}
