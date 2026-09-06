using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Lookups.Dtos;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Tickets;

/// <summary>Administration of the ticket category tree (PDF area 2).
///
/// Separate from the read-only tree on LookupsController, which every picker uses: keeping
/// the write path out of the lookup controller means a permission change there cannot
/// accidentally expose category editing.</summary>
public interface ITicketCategoryService
{
    Task<IReadOnlyList<CategoryLookupDto>> GetTreeAsync(bool includeInactive, CancellationToken ct = default);
    Task<CategoryLookupDto> CreateAsync(CategoryUpsertRequest request, CancellationToken ct = default);
    Task<CategoryLookupDto> UpdateAsync(Guid id, CategoryUpsertRequest request, CancellationToken ct = default);
    Task ReorderAsync(CategoryReorderRequest request, CancellationToken ct = default);
    Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class TicketCategoryService(IAppDbContext db, IClock clock, ICurrentUser currentUser)
    : ITicketCategoryService
{
    public async Task<IReadOnlyList<CategoryLookupDto>> GetTreeAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        var q = db.TicketCategories.AsNoTracking();
        if (!includeInactive) q = q.Where(c => c.IsActive);

        var flat = await q
            .OrderBy(c => c.SortOrder).ThenBy(c => c.NameEn)
            .Select(c => new { c.Id, c.NameAr, c.NameEn, c.ParentId, c.DepartmentId, c.SortOrder })
            .ToListAsync(ct);

        var byParent = flat.ToLookup(c => c.ParentId);

        List<CategoryLookupDto> Build(Guid? parentId) =>
            byParent[parentId]
                .Select(c => new CategoryLookupDto(
                    c.Id, c.NameAr, c.NameEn, c.ParentId, c.DepartmentId, c.SortOrder, Build(c.Id)))
                .ToList();

        return Build(null);
    }

    public async Task<CategoryLookupDto> CreateAsync(CategoryUpsertRequest request, CancellationToken ct = default)
    {
        await GuardParentAsync(request.ParentId, null, ct);
        await GuardDepartmentAsync(request.DepartmentId, ct);

        var category = new TicketCategory
        {
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            ParentId = request.ParentId,
            DepartmentId = request.DepartmentId,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive
        };

        db.TicketCategories.Add(category);
        await db.SaveChangesAsync(ct);

        return ToDto(category);
    }

    public async Task<CategoryLookupDto> UpdateAsync(
        Guid id, CategoryUpsertRequest request, CancellationToken ct = default)
    {
        var category = await db.TicketCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(TicketCategory), id);

        await GuardParentAsync(request.ParentId, id, ct);
        await GuardDepartmentAsync(request.DepartmentId, ct);

        category.NameAr = request.NameAr.Trim();
        category.NameEn = request.NameEn.Trim();
        category.ParentId = request.ParentId;
        category.DepartmentId = request.DepartmentId;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);
        return ToDto(category);
    }

    /// <summary>Applies a whole drag-and-drop reorder in one save, so the tree is never
    /// briefly persisted in a half-moved state.</summary>
    public async Task ReorderAsync(CategoryReorderRequest request, CancellationToken ct = default)
    {
        var ids = request.Items.Select(i => i.Id).ToList();

        var categories = await db.TicketCategories
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var missing = ids.Except(categories.Keys).ToList();
        if (missing.Count > 0)
            throw new NotFoundException(nameof(TicketCategory), string.Join(", ", missing));

        // Validate the whole proposed shape before writing any of it: a reorder that
        // creates a cycle halfway through would leave the tree unusable.
        var proposedParents = categories.ToDictionary(c => c.Key, c => c.Value.ParentId);
        foreach (var item in request.Items)
        {
            proposedParents[item.Id] = item.ParentId;
        }

        foreach (var item in request.Items)
        {
            if (item.ParentId is { } parentId)
            {
                if (!categories.ContainsKey(parentId)
                    && !await db.TicketCategories.AnyAsync(c => c.Id == parentId, ct))
                {
                    throw new BadRequestException("The target parent category does not exist.");
                }

                if (CreatesCycle(item.Id, parentId, proposedParents))
                    throw new BadRequestException("That move would make a category its own ancestor.");
            }
        }

        foreach (var item in request.Items)
        {
            var category = categories[item.Id];
            category.ParentId = item.ParentId;
            category.SortOrder = item.SortOrder;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deactivating hides a category from the new-ticket pickers but leaves every
    /// existing ticket's CategoryId untouched — historical reporting must not shift
    /// because someone tidied the tree.</summary>
    public async Task SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        var category = await db.TicketCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(TicketCategory), id);

        if (category.IsActive == isActive) return;

        category.IsActive = isActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.TicketCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(TicketCategory), id);

        var activeStatuses = TicketWorkflow.ActiveStatuses;

        var liveTickets = await db.Tickets
            .CountAsync(t => t.CategoryId == id && activeStatuses.Contains(t.Status), ct);

        if (liveTickets > 0)
            throw new ConflictException(
                $"{liveTickets} active ticket(s) use this category. Deactivate it instead of deleting it.");

        if (await db.TicketCategories.AnyAsync(c => c.ParentId == id, ct))
            throw new ConflictException("Move or remove the sub-categories first.");

        category.IsDeleted = true;
        category.DeletedAt = clock.UtcNow;
        category.DeletedBy = currentUser.UserId;
        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    private static CategoryLookupDto ToDto(TicketCategory c) =>
        new(c.Id, c.NameAr, c.NameEn, c.ParentId, c.DepartmentId, c.SortOrder, []);

    private async Task GuardParentAsync(Guid? parentId, Guid? selfId, CancellationToken ct)
    {
        if (parentId is not { } parent) return;

        if (parent == selfId)
            throw new BadRequestException("A category cannot be its own parent.");

        if (!await db.TicketCategories.AnyAsync(c => c.Id == parent, ct))
            throw new BadRequestException("The selected parent category does not exist.");

        if (selfId is not { } id) return;

        // Walking up from the proposed parent is enough: if this category appears in that
        // chain, the move would close a loop.
        var parents = await db.TicketCategories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);

        if (CreatesCycle(id, parent, parents))
            throw new BadRequestException("That move would make a category its own ancestor.");
    }

    private static bool CreatesCycle(Guid id, Guid proposedParent, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var seen = new HashSet<Guid>();
        Guid? cursor = proposedParent;

        while (cursor is { } current)
        {
            if (current == id) return true;

            // Guards against a pre-existing cycle in the data turning this into a hang.
            if (!seen.Add(current)) return true;

            cursor = parents.GetValueOrDefault(current);
        }

        return false;
    }

    private async Task GuardDepartmentAsync(Guid? departmentId, CancellationToken ct)
    {
        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d, ct))
            throw new BadRequestException("The selected department does not exist.");
    }
}
