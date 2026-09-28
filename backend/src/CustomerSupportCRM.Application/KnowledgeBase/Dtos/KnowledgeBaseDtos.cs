using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Application.KnowledgeBase.Dtos;

// ---- Categories ----

public sealed record ArticleCategoryDto(
    Guid Id,
    Guid? ParentId,
    string NameAr,
    string NameEn,
    int SortOrder,
    bool IsActive,
    Guid? DepartmentId,
    int ArticleCount,
    IReadOnlyList<ArticleCategoryDto> Children);

public sealed record SaveArticleCategoryRequest(
    Guid? ParentId,
    string NameAr,
    string NameEn,
    int SortOrder,
    bool IsActive,
    Guid? DepartmentId,
    Guid? BranchId);

// ---- Articles ----

public sealed record ArticleTagDto(string Slug, string LabelAr, string LabelEn);

/// <summary>The row shown in a list or a search result. Excludes the bodies, which are large
/// and unused until an article is opened.</summary>
public sealed record ArticleListItemDto(
    Guid Id,
    Guid CategoryId,
    string CategoryNameAr,
    string CategoryNameEn,
    string TitleAr,
    string TitleEn,
    string SlugAr,
    string SlugEn,
    string? SummaryAr,
    string? SummaryEn,
    ArticleStatus Status,
    bool IsFaq,
    bool IsPublic,
    long ViewCount,
    int HelpfulCount,
    int NotHelpfulCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<ArticleTagDto> Tags,
    /// <summary>A short passage around the match, for search results. Null when listing.</summary>
    string? Excerpt);

public sealed record ArticleDetailDto(
    Guid Id,
    Guid CategoryId,
    string CategoryNameAr,
    string CategoryNameEn,
    string TitleAr,
    string TitleEn,
    string SlugAr,
    string SlugEn,
    string? SummaryAr,
    string? SummaryEn,
    string BodyAr,
    string BodyEn,
    ArticleStatus Status,
    bool IsFaq,
    bool IsPublic,
    Guid? AuthorUserId,
    string? AuthorName,
    long ViewCount,
    int HelpfulCount,
    int NotHelpfulCount,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? FirstPublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<ArticleTagDto> Tags,
    /// <summary>True once the article has been published at least once, which is when the
    /// slugs freeze. The editor uses it to explain why they can no longer change.</summary>
    bool SlugsLocked);

public sealed record SaveArticleRequest(
    Guid CategoryId,
    string TitleAr,
    string TitleEn,
    string? SummaryAr,
    string? SummaryEn,
    string BodyAr,
    string BodyEn,
    bool IsFaq,
    bool IsPublic,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<ArticleTagDto>? Tags,
    /// <summary>Recorded against the version snapshot, so a reviewer can see why an article
    /// changed without diffing it.</summary>
    string? ChangeNote);

public sealed record ArticleQuery(
    string? Search = null,
    Guid? CategoryId = null,
    string? Tag = null,
    ArticleStatus? Status = null,
    bool? IsFaq = null,
    /// <summary>Restricts to what a portal visitor may see. Ignored for agent callers, who
    /// pass it false.</summary>
    bool PublicOnly = false,
    int Page = 1,
    int PageSize = 20)
{
    public int Skip => (Math.Max(Page, 1) - 1) * PageSize;
}

// ---- Versions, votes, links ----

public sealed record ArticleVersionDto(
    Guid Id,
    int VersionNumber,
    string TitleAr,
    string TitleEn,
    ArticleStatus Status,
    Guid? EditorUserId,
    string? EditorName,
    DateTimeOffset EditedAt,
    string? ChangeNote);

public sealed record ArticleVoteRequest(bool IsHelpful);

public sealed record ArticleVoteResultDto(int HelpfulCount, int NotHelpfulCount, bool YourVote);

public sealed record LinkArticleToTicketRequest(Guid ArticleId);

public sealed record ArticleTicketLinkDto(
    Guid Id,
    Guid ArticleId,
    string TitleAr,
    string TitleEn,
    string SlugAr,
    string SlugEn,
    Guid? LinkedByUserId,
    string? LinkedByName,
    DateTimeOffset LinkedAt);

/// <summary>Paged search results with the facets needed to refine them.</summary>
public sealed record ArticleSearchResultDto(
    PagedResult<ArticleListItemDto> Results,
    IReadOnlyList<ArticleTagDto> AvailableTags);
