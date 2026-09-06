namespace CustomerSupportCRM.Domain.Common;

/// <summary>Root of every persisted entity. Keys are GUIDs so records can be
/// created client-side and merged across branches without sequence collisions.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
