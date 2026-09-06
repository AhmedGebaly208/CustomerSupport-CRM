using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Department and branch data scoping (PDF area 12).
///
/// This is enforcement, not a convenience filter: a scoped agent must not be able to read
/// another department's tickets by guessing a record id. Deliberately applied explicitly at
/// the service layer rather than as an EF global query filter, because administrators need
/// a legitimate global view and global filters are awkward to opt out of safely.</summary>
public interface IScopeProvider
{
    /// <summary>True for roles that legitimately see every department and branch.</summary>
    bool IsGlobal { get; }

    Guid? DepartmentId { get; }
    Guid? BranchId { get; }

    /// <summary>Narrows a query to what the caller may see. A no-op for global callers.</summary>
    IQueryable<T> Apply<T>(IQueryable<T> source) where T : class, IScopedEntity;

    /// <summary>Throws <c>ForbiddenException</c> when the caller may not touch this record.
    /// Call it after loading by id, before returning or mutating.</summary>
    void EnsureCanAccess(IScopedEntity entity);

    /// <summary>True when the caller may see the record. Use for filtering in memory where
    /// a query cannot be composed.</summary>
    bool CanAccess(IScopedEntity entity);
}
