using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>A branch of the knowledge-base taxonomy (PDF area 6).
///
/// Separate from TicketCategory on purpose: how customers look for an answer and how the
/// desk routes work are different questions, and forcing one tree to serve both makes each
/// worse.</summary>
public class ArticleCategory : AuditableEntity, IScopedEntity
{
    public Guid? ParentId { get; set; }
    public ArticleCategory? Parent { get; set; }
    public ICollection<ArticleCategory> Children { get; set; } = new List<ArticleCategory>();

    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Null means the category is visible desk-wide; set, it belongs to one
    /// department's private taxonomy.</summary>
    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }

    public ICollection<Article> Articles { get; set; } = new List<Article>();
}

/// <summary>A knowledge-base article: an FAQ, a help page, or a step-by-step guide
/// (PDF area 6). One body of content serves both agents and the customer portal.</summary>
public class Article : AuditableEntity, IScopedEntity
{
    public Guid CategoryId { get; set; }
    public ArticleCategory? Category { get; set; }

    /// <summary>Identity user id of the author, matching TicketComment.AuthorId.</summary>
    public Guid? AuthorUserId { get; set; }

    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;

    /// <summary>Url-safe identifiers, one per language. Frozen at first publish so a link
    /// shared with a customer keeps working after an editorial retitle.</summary>
    public string SlugAr { get; set; } = string.Empty;
    public string SlugEn { get; set; } = string.Empty;

    public string? SummaryAr { get; set; }
    public string? SummaryEn { get; set; }

    public string BodyAr { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;

    /// <summary>Plain-text, normalised projections of title, summary and body. Written on
    /// save so a search never has to strip markup or fold letters at query time.</summary>
    public string SearchTextAr { get; set; } = string.Empty;
    public string SearchTextEn { get; set; } = string.Empty;

    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;

    /// <summary>Short answers surfaced as an FAQ list rather than browsed by category.</summary>
    public bool IsFaq { get; set; }

    /// <summary>Visible to customers in the portal. Internal articles stay agent-only —
    /// runbooks and workarounds are not customer-facing.</summary>
    public bool IsPublic { get; set; } = true;

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Set once, the first time the article is published. Slugs freeze from then on.</summary>
    public DateTimeOffset? FirstPublishedAt { get; set; }

    public long ViewCount { get; set; }
    public int HelpfulCount { get; set; }
    public int NotHelpfulCount { get; set; }

    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }

    public ICollection<ArticleTag> Tags { get; set; } = new List<ArticleTag>();
    public ICollection<ArticleVersion> Versions { get; set; } = new List<ArticleVersion>();
}

/// <summary>A facet on an article. A join table rather than a comma-separated column so
/// filtering by tag stays an indexed lookup.</summary>
public class ArticleTag : BaseEntity
{
    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    /// <summary>Normalised form, used for matching.</summary>
    public string Slug { get; set; } = string.Empty;

    public string LabelAr { get; set; } = string.Empty;
    public string LabelEn { get; set; } = string.Empty;
}

/// <summary>An append-only snapshot of an article as it stood before an edit.
///
/// Published help is something customers act on, so being able to see what it said last
/// month — and restore it — matters more than saving the rows.</summary>
public class ArticleVersion : BaseEntity
{
    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public int VersionNumber { get; set; }

    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string? SummaryAr { get; set; }
    public string? SummaryEn { get; set; }
    public string BodyAr { get; set; } = string.Empty;
    public string BodyEn { get; set; } = string.Empty;

    public ArticleStatus Status { get; set; }

    public Guid? EditorUserId { get; set; }
    public DateTimeOffset EditedAt { get; set; }
    public string? ChangeNote { get; set; }
}

/// <summary>One "was this helpful?" answer.
///
/// Keyed by a voter key rather than a user id so portal visitors who are not signed in can
/// still answer, while still being stopped from voting twice.</summary>
public class ArticleVote : BaseEntity
{
    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    /// <summary>A signed-in user's id, or an anonymous browser key. Never a raw IP address —
    /// that would make the table a log of who read what.</summary>
    public string VoterKey { get; set; } = string.Empty;

    public bool IsHelpful { get; set; }
    public DateTimeOffset VotedAt { get; set; }
}

/// <summary>Records that an agent used an article while answering a ticket (PDF area 6).
///
/// This is what makes the knowledge base measurable: which articles actually deflect work,
/// and which tickets keep being answered without one.</summary>
public class ArticleTicketLink : BaseEntity
{
    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }

    public Guid? LinkedByUserId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
