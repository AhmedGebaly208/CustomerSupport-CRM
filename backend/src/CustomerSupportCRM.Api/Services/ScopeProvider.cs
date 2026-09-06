using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Api.Services;

/// <summary>Resolves the caller's department/branch scope from their JWT claims.
///
/// Two decisions worth knowing about, both deliberate:
///
/// 1. A non-global caller with no department claim sees nothing rather than everything.
///    Failing closed means a misconfigured agent account is an obvious support ticket,
///    not a silent data leak.
///
/// 2. Records with no department are visible to every scoped caller. A ticket that has not
///    been routed yet has to be claimable by someone, and the agent dashboard already
///    surfaces an "unassigned in department" queue. Hiding unrouted work from everyone
///    would strand it.</summary>
public sealed class ScopeProvider(ICurrentUser currentUser) : IScopeProvider
{
    /// <summary>The cross-department view is the same capability as seeing another agent's
    /// dashboard, so it keys off that permission rather than a role list — a future custom
    /// role gets the global view by being granted the permission, not by being special-cased
    /// here.</summary>
    public bool IsGlobal => currentUser.HasPermission(Permissions.Dashboard.ViewTeam);

    public Guid? DepartmentId => ParseClaim("department_id");

    public Guid? BranchId => ParseClaim("branch_id");

    public IQueryable<T> Apply<T>(IQueryable<T> source) where T : class, IScopedEntity
    {
        if (IsGlobal) return source;

        var departmentId = DepartmentId;

        // Fail closed: no scope means no rows, not all rows.
        if (departmentId is null) return source.Where(_ => false);

        return source.Where(e => e.DepartmentId == null || e.DepartmentId == departmentId);
    }

    public bool CanAccess(IScopedEntity entity)
    {
        if (IsGlobal) return true;

        var departmentId = DepartmentId;
        if (departmentId is null) return false;

        return entity.DepartmentId is null || entity.DepartmentId == departmentId;
    }

    public void EnsureCanAccess(IScopedEntity entity)
    {
        if (!CanAccess(entity))
            throw new ForbiddenException("This record belongs to another department.");
    }

    private Guid? ParseClaim(string claimType)
    {
        // ICurrentUser deliberately exposes only the claims Application needs; read the
        // scope claims through the same abstraction rather than reaching for HttpContext.
        var raw = currentUser.FindClaim(claimType);
        return Guid.TryParse(raw, out var value) ? value : null;
    }
}
