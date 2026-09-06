namespace CustomerSupportCRM.Domain.Common;

/// <summary>Support records are never hard-deleted: ticket and customer history has to
/// survive for SLA reporting and audit. A global query filter hides flagged rows.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
