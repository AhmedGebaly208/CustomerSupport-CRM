using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Lookups.Dtos;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Lookups;

public interface ILookupService
{
    Task<IReadOnlyList<LookupDto>> GetDepartmentsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupDto>> GetBranchesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CategoryLookupDto>> GetCategoryTreeAsync(Guid? departmentId, CancellationToken ct = default);
    IReadOnlyDictionary<string, IReadOnlyList<EnumOptionDto>> GetEnums();
}

public sealed class LookupService(IAppDbContext db) : ILookupService
{
    public async Task<IReadOnlyList<LookupDto>> GetDepartmentsAsync(CancellationToken ct = default) =>
        await db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.NameEn)
            .Select(d => new LookupDto(d.Id, d.NameAr, d.NameEn, d.Code))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LookupDto>> GetBranchesAsync(CancellationToken ct = default) =>
        await db.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.NameEn)
            .Select(b => new LookupDto(b.Id, b.NameAr, b.NameEn, b.Code))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CategoryLookupDto>> GetCategoryTreeAsync(Guid? departmentId, CancellationToken ct = default)
    {
        var q = db.TicketCategories.AsNoTracking().Where(c => c.IsActive);
        if (departmentId is { } id)
            q = q.Where(c => c.DepartmentId == id || c.DepartmentId == null);

        var flat = await q
            .OrderBy(c => c.SortOrder).ThenBy(c => c.NameEn)
            .Select(c => new { c.Id, c.NameAr, c.NameEn, c.ParentId, c.DepartmentId, c.SortOrder })
            .ToListAsync(ct);

        // The tree is small enough (tens of rows) that building it in memory beats a
        // recursive CTE and keeps the query provider-agnostic for tests.
        var byParent = flat.ToLookup(c => c.ParentId);

        List<CategoryLookupDto> Build(Guid? parentId) =>
            byParent[parentId]
                .Select(c => new CategoryLookupDto(c.Id, c.NameAr, c.NameEn, c.ParentId, c.DepartmentId, c.SortOrder, Build(c.Id)))
                .ToList();

        return Build(null);
    }

    /// <summary>Server-owned enum options so the Vue pickers never drift out of sync with
    /// the backend definitions.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EnumOptionDto>> GetEnums() =>
        new Dictionary<string, IReadOnlyList<EnumOptionDto>>
        {
            ["ticketStatus"] = Options<TicketStatus>(),
            ["ticketPriority"] = Options<TicketPriority>(),
            ["channel"] = Options<CommunicationChannel>(),
            ["contactType"] = Options<ContactType>(),
            ["interactionDirection"] = Options<InteractionDirection>()
        };

    private static IReadOnlyList<EnumOptionDto> Options<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .Select(v => new EnumOptionDto(Convert.ToInt32(v), v.ToString()!))
            .ToList();
}
