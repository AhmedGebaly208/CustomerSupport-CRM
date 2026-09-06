namespace CustomerSupportCRM.Domain.Common;

/// <summary>Convenience base for the common case: auditable and soft-deletable.</summary>
public abstract class AuditableEntity : BaseEntity, IAuditableEntity, ISoftDeletable
{
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public Guid? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
