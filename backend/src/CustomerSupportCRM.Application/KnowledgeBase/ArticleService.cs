using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.KnowledgeBase.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Domain.KnowledgeBase;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.KnowledgeBase;

public interface IArticleService
{
    Task<ArticleSearchResultDto> SearchAsync(ArticleQuery query, CancellationToken ct = default);
    Task<ArticleDetailDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ArticleDetailDto> GetBySlugAsync(string slug, CancellationToken ct = default);

    Task<ArticleDetailDto> CreateAsync(SaveArticleRequest request, CancellationToken ct = default);
    Task<ArticleDetailDto> UpdateAsync(Guid id, SaveArticleRequest request, CancellationToken ct = default);
    Task<ArticleDetailDto> SetStatusAsync(Guid id, ArticleStatus status, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ArticleVersionDto>> ListVersionsAsync(Guid id, CancellationToken ct = default);
    Task<ArticleDetailDto> RestoreVersionAsync(Guid id, Guid versionId, CancellationToken ct = default);

    Task RecordViewAsync(Guid id, CancellationToken ct = default);
    Task<ArticleVoteResultDto> VoteAsync(Guid id, ArticleVoteRequest request, CancellationToken ct = default);

    Task<ArticleTicketLinkDto> LinkToTicketAsync(Guid ticketId, Guid articleId, CancellationToken ct = default);
    Task<IReadOnlyList<ArticleTicketLinkDto>> ListTicketLinksAsync(Guid ticketId, CancellationToken ct = default);
    Task UnlinkFromTicketAsync(Guid ticketId, Guid linkId, CancellationToken ct = default);
}

/// <summary>The knowledge base (PDF area 6).
///
/// Search runs against normalised plain-text columns written on save rather than a SQL
/// full-text catalog. A catalog is a server-level feature that is absent from a default SQL
/// Express install, and an article base is small enough that a prefix scan over a stored
/// projection answers fast while working the same way on every deployment.</summary>
public sealed class ArticleService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IIdentityService identity,
    IScopeProvider scope,
    IClock clock) : IArticleService
{
    /// <summary>Characters of body text either side of a match in a search excerpt.</summary>
    private const int ExcerptRadius = 90;

    // ---- Reading ----

    public async Task<ArticleSearchResultDto> SearchAsync(ArticleQuery query, CancellationToken ct = default)
    {
        var q = Readable(query.PublicOnly);

        if (query.CategoryId is { } categoryId) q = q.Where(a => a.CategoryId == categoryId);
        if (query.Status is { } status) q = q.Where(a => a.Status == status);
        if (query.IsFaq is { } isFaq) q = q.Where(a => a.IsFaq == isFaq);

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = ArabicTextNormalizer.Normalize(query.Tag);
            q = q.Where(a => a.Tags.Any(t => t.Slug == tag));
        }

        var term = ArabicTextNormalizer.Normalize(query.Search);

        if (term.Length > 0)
        {
            // Matched against both languages: an agent searching in Arabic should still find
            // an article whose English side names the product.
            q = q.Where(a => a.SearchTextAr.Contains(term) || a.SearchTextEn.Contains(term));
        }

        var total = await q.CountAsync(ct);

        var ordered = term.Length > 0
            // A title hit is a better answer than a passing mention in the body.
            ? q.OrderByDescending(a => a.TitleAr.Contains(term) || a.TitleEn.Contains(term))
                .ThenByDescending(a => a.HelpfulCount - a.NotHelpfulCount)
                .ThenByDescending(a => a.ViewCount)
            : q.OrderByDescending(a => a.IsFaq)
                .ThenByDescending(a => a.HelpfulCount - a.NotHelpfulCount)
                .ThenByDescending(a => a.PublishedAt);

        var rows = await ordered
            .Skip(query.Skip)
            .Take(Math.Clamp(query.PageSize, 1, 100))
            .Include(a => a.Category)
            .Include(a => a.Tags)
            .ToListAsync(ct);

        var items = rows.Select(a => ToListItem(a, term)).ToList();

        // Facets come from the matched set, not the whole base, so refining never leads to
        // an empty result.
        var tags = rows
            .SelectMany(a => a.Tags)
            .GroupBy(t => t.Slug)
            .Select(g => new ArticleTagDto(g.Key, g.First().LabelAr, g.First().LabelEn))
            .OrderBy(t => t.LabelEn)
            .ToList();

        return new ArticleSearchResultDto(
            PagedResult<ArticleListItemDto>.Create(items, total, query.Page, query.PageSize),
            tags);
    }

    public async Task<ArticleDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var article = await Readable(publicOnly: false)
            .Include(a => a.Category)
            .Include(a => a.Tags)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Article), id);

        return await ToDetailAsync(article, ct);
    }

    public async Task<ArticleDetailDto> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        var normalized = ArabicTextNormalizer.Normalize(slug);

        var article = await Readable(publicOnly: false)
            .Include(a => a.Category)
            .Include(a => a.Tags)
            .FirstOrDefaultAsync(a => a.SlugAr == normalized || a.SlugEn == normalized, ct)
            ?? throw new NotFoundException(nameof(Article), slug);

        return await ToDetailAsync(article, ct);
    }

    // ---- Writing ----

    public async Task<ArticleDetailDto> CreateAsync(SaveArticleRequest request, CancellationToken ct = default)
    {
        await GuardAsync(request, ct);

        var article = new Article
        {
            AuthorUserId = currentUser.UserId,
            Status = ArticleStatus.Draft
        };

        Apply(article, request);

        article.SlugAr = await UniqueSlugAsync(request.TitleAr, "ar", null, ct);
        article.SlugEn = await UniqueSlugAsync(request.TitleEn, "en", null, ct);

        db.Articles.Add(article);
        ReplaceTags(article, request.Tags);

        await db.SaveChangesAsync(ct);
        await SnapshotAsync(article, request.ChangeNote, ct);

        return await GetAsync(article.Id, ct);
    }

    public async Task<ArticleDetailDto> UpdateAsync(
        Guid id, SaveArticleRequest request, CancellationToken ct = default)
    {
        await GuardAsync(request, ct);

        var article = await db.Articles
            .Include(a => a.Tags)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Article), id);

        scope.EnsureCanAccess(article);

        // Snapshot the state being replaced, not the new one.
        await SnapshotAsync(article, request.ChangeNote, ct);

        Apply(article, request);

        // Slugs freeze at first publish: a link already shared with a customer, or cited in
        // a closed ticket, must keep resolving after an editorial retitle.
        if (article.FirstPublishedAt is null)
        {
            article.SlugAr = await UniqueSlugAsync(request.TitleAr, "ar", article.Id, ct);
            article.SlugEn = await UniqueSlugAsync(request.TitleEn, "en", article.Id, ct);
        }

        // Replacements go through the set, not the navigation: ids are generated in the
        // domain, and an entity reached through a tracked parent with its key set is read as
        // an existing row, turning the insert into an update of a row that was never written.
        db.ArticleTags.RemoveRange(article.Tags);
        article.Tags.Clear();
        db.ArticleTags.AddRange(BuildTags(article.Id, request.Tags));

        await db.SaveChangesAsync(ct);
        return await GetAsync(article.Id, ct);
    }

    public async Task<ArticleDetailDto> SetStatusAsync(
        Guid id, ArticleStatus status, CancellationToken ct = default)
    {
        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Article), id);

        scope.EnsureCanAccess(article);

        if (article.Status == status) return await GetAsync(id, ct);

        var now = clock.UtcNow;

        if (status == ArticleStatus.Published)
        {
            if (string.IsNullOrWhiteSpace(article.BodyAr) || string.IsNullOrWhiteSpace(article.BodyEn))
            {
                // Publishing a half-translated article shows one audience an empty page.
                throw new BadRequestException(
                    "An article needs a body in both Arabic and English before it can be published.",
                    ErrorCodes.ArticleIncomplete);
            }

            article.PublishedAt = now;
            article.FirstPublishedAt ??= now;
        }

        article.Status = status;
        await db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Article), id);

        scope.EnsureCanAccess(article);

        var linked = await db.ArticleTicketLinks.CountAsync(l => l.ArticleId == id, ct);

        if (linked > 0)
        {
            // Tickets cite this article as their answer. Removing it would leave those
            // tickets pointing at nothing; archiving keeps the history readable.
            throw new ConflictException(
                $"{linked} ticket(s) reference this article. Archive it instead of deleting it.",
                ErrorCodes.ArticleInUse);
        }

        article.IsDeleted = true;
        article.DeletedAt = clock.UtcNow;
        article.DeletedBy = currentUser.UserId;

        await db.SaveChangesAsync(ct);
    }

    // ---- Versions ----

    public async Task<IReadOnlyList<ArticleVersionDto>> ListVersionsAsync(
        Guid id, CancellationToken ct = default)
    {
        await EnsureReadableAsync(id, ct);

        var rows = await db.ArticleVersions.AsNoTracking()
            .Where(v => v.ArticleId == id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

        var names = await identity.GetUserDisplayNamesAsync(
            rows.Where(r => r.EditorUserId is not null).Select(r => r.EditorUserId!.Value), ct);

        return rows.Select(v => new ArticleVersionDto(
            v.Id, v.VersionNumber, v.TitleAr, v.TitleEn, v.Status, v.EditorUserId,
            v.EditorUserId is { } e && names.TryGetValue(e, out var n) ? n : null,
            v.EditedAt, v.ChangeNote)).ToList();
    }

    public async Task<ArticleDetailDto> RestoreVersionAsync(
        Guid id, Guid versionId, CancellationToken ct = default)
    {
        var article = await db.Articles.Include(a => a.Tags)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Article), id);

        scope.EnsureCanAccess(article);

        var version = await db.ArticleVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId && v.ArticleId == id, ct)
            ?? throw new NotFoundException(nameof(ArticleVersion), versionId);

        // The current text is snapshotted first, so restoring is itself undoable.
        await SnapshotAsync(article, $"Restored version {version.VersionNumber}", ct);

        article.TitleAr = version.TitleAr;
        article.TitleEn = version.TitleEn;
        article.SummaryAr = version.SummaryAr;
        article.SummaryEn = version.SummaryEn;
        article.BodyAr = version.BodyAr;
        article.BodyEn = version.BodyEn;
        RebuildSearchText(article);

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---- Engagement ----

    public async Task RecordViewAsync(Guid id, CancellationToken ct = default)
    {
        // An atomic increment rather than load-modify-save: views race by nature, and the
        // count is not worth a transaction.
        await db.Articles
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ViewCount, a => a.ViewCount + 1), ct);
    }

    public async Task<ArticleVoteResultDto> VoteAsync(
        Guid id, ArticleVoteRequest request, CancellationToken ct = default)
    {
        var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Article), id);

        var voterKey = currentUser.UserId?.ToString()
            ?? throw new ForbiddenException("Not authenticated.", ErrorCodes.NotAuthenticated);

        var existing = await db.ArticleVotes
            .FirstOrDefaultAsync(v => v.ArticleId == id && v.VoterKey == voterKey, ct);

        if (existing is null)
        {
            db.ArticleVotes.Add(new ArticleVote
            {
                ArticleId = id,
                VoterKey = voterKey,
                IsHelpful = request.IsHelpful,
                VotedAt = clock.UtcNow
            });

            if (request.IsHelpful) article.HelpfulCount++;
            else article.NotHelpfulCount++;
        }
        else if (existing.IsHelpful != request.IsHelpful)
        {
            // Changing your mind moves the vote rather than adding a second one.
            existing.IsHelpful = request.IsHelpful;
            existing.VotedAt = clock.UtcNow;

            if (request.IsHelpful)
            {
                article.HelpfulCount++;
                article.NotHelpfulCount = Math.Max(0, article.NotHelpfulCount - 1);
            }
            else
            {
                article.NotHelpfulCount++;
                article.HelpfulCount = Math.Max(0, article.HelpfulCount - 1);
            }
        }

        await db.SaveChangesAsync(ct);

        return new ArticleVoteResultDto(article.HelpfulCount, article.NotHelpfulCount, request.IsHelpful);
    }

    // ---- Ticket links ----

    public async Task<ArticleTicketLinkDto> LinkToTicketAsync(
        Guid ticketId, Guid articleId, CancellationToken ct = default)
    {
        var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new NotFoundException(nameof(Ticket), ticketId);

        scope.EnsureCanAccess(ticket);
        await EnsureReadableAsync(articleId, ct);

        var existing = await db.ArticleTicketLinks
            .FirstOrDefaultAsync(l => l.ArticleId == articleId && l.TicketId == ticketId, ct);

        if (existing is null)
        {
            db.ArticleTicketLinks.Add(new ArticleTicketLink
            {
                ArticleId = articleId,
                TicketId = ticketId,
                LinkedByUserId = currentUser.UserId,
                LinkedAt = clock.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }

        return (await ListTicketLinksAsync(ticketId, ct)).First(l => l.ArticleId == articleId);
    }

    public async Task<IReadOnlyList<ArticleTicketLinkDto>> ListTicketLinksAsync(
        Guid ticketId, CancellationToken ct = default)
    {
        var rows = await db.ArticleTicketLinks.AsNoTracking()
            .Where(l => l.TicketId == ticketId)
            .OrderByDescending(l => l.LinkedAt)
            .Select(l => new
            {
                l.Id, l.ArticleId, l.LinkedByUserId, l.LinkedAt,
                l.Article!.TitleAr, l.Article.TitleEn, l.Article.SlugAr, l.Article.SlugEn
            })
            .ToListAsync(ct);

        var names = await identity.GetUserDisplayNamesAsync(
            rows.Where(r => r.LinkedByUserId is not null).Select(r => r.LinkedByUserId!.Value), ct);

        return rows.Select(r => new ArticleTicketLinkDto(
            r.Id, r.ArticleId, r.TitleAr, r.TitleEn, r.SlugAr, r.SlugEn,
            r.LinkedByUserId,
            r.LinkedByUserId is { } u && names.TryGetValue(u, out var n) ? n : null,
            r.LinkedAt)).ToList();
    }

    public async Task UnlinkFromTicketAsync(Guid ticketId, Guid linkId, CancellationToken ct = default)
    {
        var link = await db.ArticleTicketLinks
            .FirstOrDefaultAsync(l => l.Id == linkId && l.TicketId == ticketId, ct)
            ?? throw new NotFoundException(nameof(ArticleTicketLink), linkId);

        db.ArticleTicketLinks.Remove(link);
        await db.SaveChangesAsync(ct);
    }

    // ---- helpers ----

    /// <summary>Articles the caller may see. Agents see drafts inside their scope; a portal
    /// read passes publicOnly and gets only published, public articles.</summary>
    private IQueryable<Article> Readable(bool publicOnly)
    {
        if (publicOnly)
        {
            // Department scoping is deliberately not applied here. A published, public
            // article is customer-facing help, not a department's private material, and the
            // reader is typically a portal customer with no department at all — scope fails
            // closed for them, which would hide every article and leave the help page empty.
            // The Published and IsPublic pair is the whole access rule for this path.
            return db.Articles.AsNoTracking()
                .Where(a => a.Status == ArticleStatus.Published && a.IsPublic);
        }

        // Staff reads stay scoped: drafts and internal articles belong to the department
        // that wrote them.
        return scope.Apply(db.Articles.AsNoTracking());
    }

    private async Task EnsureReadableAsync(Guid id, CancellationToken ct)
    {
        var article = await db.Articles.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(nameof(Article), id);

        scope.EnsureCanAccess(article);
    }

    private async Task GuardAsync(SaveArticleRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TitleAr) || string.IsNullOrWhiteSpace(request.TitleEn))
            throw new BadRequestException("An article needs a title in both languages.");

        if (!await db.ArticleCategories.AnyAsync(c => c.Id == request.CategoryId, ct))
            throw new NotFoundException(nameof(ArticleCategory), request.CategoryId);
    }

    private static void Apply(Article article, SaveArticleRequest request)
    {
        article.CategoryId = request.CategoryId;
        article.TitleAr = request.TitleAr.Trim();
        article.TitleEn = request.TitleEn.Trim();
        article.SummaryAr = string.IsNullOrWhiteSpace(request.SummaryAr) ? null : request.SummaryAr.Trim();
        article.SummaryEn = string.IsNullOrWhiteSpace(request.SummaryEn) ? null : request.SummaryEn.Trim();
        article.BodyAr = request.BodyAr ?? string.Empty;
        article.BodyEn = request.BodyEn ?? string.Empty;
        article.IsFaq = request.IsFaq;
        article.IsPublic = request.IsPublic;
        article.DepartmentId = request.DepartmentId;
        article.BranchId = request.BranchId;

        RebuildSearchText(article);
    }

    /// <summary>Recomputes the searchable projection. Title and summary are folded in with
    /// the body so a title-only match is still found.</summary>
    private static void RebuildSearchText(Article article)
    {
        article.SearchTextAr = ArabicTextNormalizer.BuildSearchText(
            $"{article.TitleAr} {article.SummaryAr} {article.BodyAr}");

        article.SearchTextEn = ArabicTextNormalizer.BuildSearchText(
            $"{article.TitleEn} {article.SummaryEn} {article.BodyEn}");
    }

    private static List<ArticleTag> BuildTags(Guid articleId, IReadOnlyList<ArticleTagDto>? tags)
    {
        if (tags is null) return [];

        return tags
            .Select(t => new
            {
                Slug = ArabicTextNormalizer.Slugify(
                    string.IsNullOrWhiteSpace(t.Slug) ? t.LabelEn : t.Slug, "en"),
                t.LabelAr,
                t.LabelEn
            })
            .Where(t => t.Slug.Length > 0)
            // Distinct by slug: the unique index would otherwise reject the whole save
            // because someone typed the same tag twice.
            .GroupBy(t => t.Slug)
            .Select(g => new ArticleTag
            {
                ArticleId = articleId,
                Slug = g.Key,
                LabelAr = g.First().LabelAr,
                LabelEn = g.First().LabelEn
            })
            .ToList();
    }

    private void ReplaceTags(Article article, IReadOnlyList<ArticleTagDto>? tags)
    {
        foreach (var tag in BuildTags(article.Id, tags)) article.Tags.Add(tag);
    }

    private async Task<string> UniqueSlugAsync(string title, string culture, Guid? excludeId, CancellationToken ct)
    {
        var baseSlug = ArabicTextNormalizer.Slugify(title, culture);
        if (baseSlug.Length == 0) baseSlug = culture == "ar" ? "مقال" : "article";

        var slug = baseSlug;

        // Suffix until free. Bounded so a pathological collision cannot spin forever.
        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = slug;

            var taken = culture == "ar"
                ? await db.Articles.AnyAsync(a => a.SlugAr == candidate && (excludeId == null || a.Id != excludeId), ct)
                : await db.Articles.AnyAsync(a => a.SlugEn == candidate && (excludeId == null || a.Id != excludeId), ct);

            if (!taken) return candidate;

            slug = $"{baseSlug}-{suffix}";
        }

        // Effectively unreachable; a guid tail guarantees termination rather than throwing.
        return $"{baseSlug}-{Guid.NewGuid():N}"[..Math.Min(160, baseSlug.Length + 33)];
    }

    private async Task SnapshotAsync(Article article, string? changeNote, CancellationToken ct)
    {
        var next = await db.ArticleVersions
            .Where(v => v.ArticleId == article.Id)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        db.ArticleVersions.Add(new ArticleVersion
        {
            ArticleId = article.Id,
            VersionNumber = next + 1,
            TitleAr = article.TitleAr,
            TitleEn = article.TitleEn,
            SummaryAr = article.SummaryAr,
            SummaryEn = article.SummaryEn,
            BodyAr = article.BodyAr,
            BodyEn = article.BodyEn,
            Status = article.Status,
            EditorUserId = currentUser.UserId,
            EditedAt = clock.UtcNow,
            ChangeNote = changeNote
        });

        await db.SaveChangesAsync(ct);
    }

    private static ArticleListItemDto ToListItem(Article a, string term) => new(
        a.Id, a.CategoryId, a.Category?.NameAr ?? string.Empty, a.Category?.NameEn ?? string.Empty,
        a.TitleAr, a.TitleEn, a.SlugAr, a.SlugEn, a.SummaryAr, a.SummaryEn,
        a.Status, a.IsFaq, a.IsPublic, a.ViewCount, a.HelpfulCount, a.NotHelpfulCount,
        a.PublishedAt, a.ModifiedAt,
        a.Tags.Select(t => new ArticleTagDto(t.Slug, t.LabelAr, t.LabelEn)).ToList(),
        term.Length > 0 ? BuildExcerpt(a, term) : null);

    /// <summary>A passage around the first match, so a result list shows why it matched
    /// rather than repeating the summary.</summary>
    private static string? BuildExcerpt(Article article, string term)
    {
        foreach (var text in new[] { article.SearchTextAr, article.SearchTextEn })
        {
            var index = text.IndexOf(term, StringComparison.Ordinal);
            if (index < 0) continue;

            var start = Math.Max(0, index - ExcerptRadius);
            var length = Math.Min(text.Length - start, term.Length + ExcerptRadius * 2);

            var excerpt = text.Substring(start, length).Trim();

            return (start > 0 ? "…" : "") + excerpt + (start + length < text.Length ? "…" : "");
        }

        return null;
    }

    private async Task<ArticleDetailDto> ToDetailAsync(Article a, CancellationToken ct)
    {
        string? authorName = null;

        if (a.AuthorUserId is { } authorId)
        {
            var names = await identity.GetUserDisplayNamesAsync([authorId], ct);
            names.TryGetValue(authorId, out authorName);
        }

        return new ArticleDetailDto(
            a.Id, a.CategoryId, a.Category?.NameAr ?? string.Empty, a.Category?.NameEn ?? string.Empty,
            a.TitleAr, a.TitleEn, a.SlugAr, a.SlugEn, a.SummaryAr, a.SummaryEn,
            a.BodyAr, a.BodyEn, a.Status, a.IsFaq, a.IsPublic,
            a.AuthorUserId, authorName,
            a.ViewCount, a.HelpfulCount, a.NotHelpfulCount,
            a.PublishedAt, a.FirstPublishedAt, a.CreatedAt, a.ModifiedAt,
            a.DepartmentId, a.BranchId,
            a.Tags.Select(t => new ArticleTagDto(t.Slug, t.LabelAr, t.LabelEn)).ToList(),
            a.FirstPublishedAt is not null);
    }
}
