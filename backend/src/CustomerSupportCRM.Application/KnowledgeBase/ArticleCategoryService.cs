using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.KnowledgeBase.Dtos;
using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.KnowledgeBase;

public interface IArticleCategoryService
{
    Task<IReadOnlyList<ArticleCategoryDto>> GetTreeAsync(bool activeOnly, CancellationToken ct = default);
    Task<ArticleCategoryDto> CreateAsync(SaveArticleCategoryRequest request, CancellationToken ct = default);
    Task<ArticleCategoryDto> UpdateAsync(Guid id, SaveArticleCategoryRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>The knowledge-base taxonomy (PDF area 6). Mirrors the ticket-category service's
/// rules, including the cycle guard — a category that is its own ancestor makes the tree
/// impossible to render.</summary>
public sealed class ArticleCategoryService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IScopeProvider scope,
    IClock clock) : IArticleCategoryService
{
    public async Task<IReadOnlyList<ArticleCategoryDto>> GetTreeAsync(
        bool activeOnly, CancellationToken ct = default)
    {
        var q = scope.Apply(db.ArticleCategories.AsNoTracking());
        if (activeOnly) q = q.Where(c => c.IsActive);

        var rows = await q.OrderBy(c => c.SortOrder).ThenBy(c => c.NameEn).ToListAsync(ct);

        var counts = await db.Articles.AsNoTracking()
            .GroupBy(a => a.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

        return BuildTree(rows, null, counts);
    }

    public async Task<ArticleCategoryDto> CreateAsync(
        SaveArticleCategoryRequest request, CancellationToken ct = default)
    {
        Guard(request);
        await GuardParentAsync(request.ParentId, null, ct);

        var category = new ArticleCategory();
        Apply(category, request);

        db.ArticleCategories.Add(category);
        await db.SaveChangesAsync(ct);

        return await FindAsync(category.Id, ct);
    }

    public async Task<ArticleCategoryDto> UpdateAsync(
        Guid id, SaveArticleCategoryRequest request, CancellationToken ct = default)
    {
        Guard(request);

        var category = await db.ArticleCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(ArticleCategory), id);

        scope.EnsureCanAccess(category);
        await GuardParentAsync(request.ParentId, id, ct);

        Apply(category, request);
        await db.SaveChangesAsync(ct);

        return await FindAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var category = await db.ArticleCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(ArticleCategory), id);

        scope.EnsureCanAccess(category);

        if (await db.ArticleCategories.AnyAsync(c => c.ParentId == id, ct))
            throw new ConflictException("Move or remove the sub-categories first.");

        var articles = await db.Articles.CountAsync(a => a.CategoryId == id, ct);

        if (articles > 0)
        {
            throw new ConflictException(
                $"{articles} article(s) use this category. Move them first, or deactivate it instead.");
        }

        category.IsDeleted = true;
        category.DeletedAt = clock.UtcNow;
        category.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    private static void Guard(SaveArticleCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
            throw new BadRequestException("A category needs a name in both languages.");
    }

    /// <summary>Walks the proposed parent chain before writing, so a move that would make a
    /// category its own ancestor is refused rather than corrupting the tree.</summary>
    private async Task GuardParentAsync(Guid? parentId, Guid? id, CancellationToken ct)
    {
        if (parentId is null) return;

        if (parentId == id)
            throw new BadRequestException("A category cannot be its own parent.");

        if (!await db.ArticleCategories.AnyAsync(c => c.Id == parentId, ct))
            throw new NotFoundException(nameof(ArticleCategory), parentId);

        if (id is null) return;

        var parents = await db.ArticleCategories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToDictionaryAsync(c => c.Id, c => c.ParentId, ct);

        var cursor = parentId;
        var guard = 0;

        while (cursor is not null && guard++ < parents.Count + 1)
        {
            if (cursor == id)
                throw new BadRequestException("That move would make a category its own ancestor.");

            cursor = parents.TryGetValue(cursor.Value, out var next) ? next : null;
        }
    }

    private static void Apply(ArticleCategory category, SaveArticleCategoryRequest request)
    {
        category.ParentId = request.ParentId;
        category.NameAr = request.NameAr.Trim();
        category.NameEn = request.NameEn.Trim();
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.DepartmentId = request.DepartmentId;
        category.BranchId = request.BranchId;
    }

    private async Task<ArticleCategoryDto> FindAsync(Guid id, CancellationToken ct)
    {
        var tree = await GetTreeAsync(activeOnly: false, ct);

        return Flatten(tree).FirstOrDefault(c => c.Id == id)
            ?? throw new NotFoundException(nameof(ArticleCategory), id);
    }

    private static IEnumerable<ArticleCategoryDto> Flatten(IEnumerable<ArticleCategoryDto> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private static List<ArticleCategoryDto> BuildTree(
        List<ArticleCategory> rows, Guid? parentId, IReadOnlyDictionary<Guid, int> counts) =>
        rows.Where(c => c.ParentId == parentId)
            .Select(c => new ArticleCategoryDto(
                c.Id, c.ParentId, c.NameAr, c.NameEn, c.SortOrder, c.IsActive, c.DepartmentId,
                counts.GetValueOrDefault(c.Id),
                BuildTree(rows, c.Id, counts)))
            .ToList();
}
