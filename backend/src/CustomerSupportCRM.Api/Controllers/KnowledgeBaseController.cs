using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.KnowledgeBase;
using CustomerSupportCRM.Application.KnowledgeBase.Dtos;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Knowledge-base articles (PDF area 6).</summary>
[ApiController]
[Route("api/kb/articles")]
[Authorize]
public sealed class ArticlesController(IArticleService articles) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.KnowledgeBase.View)]
    public async Task<ActionResult<ArticleSearchResultDto>> Search(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? tag,
        [FromQuery] ArticleStatus? status,
        [FromQuery] bool? isFaq,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken ct) =>
        Ok(await articles.SearchAsync(
            new ArticleQuery(search, categoryId, tag, status, isFaq,
                PublicOnly: false, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), ct));

    [HttpGet("{id:guid}")]
    [Authorize(Permissions.KnowledgeBase.View)]
    public async Task<ActionResult<ArticleDetailDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await articles.GetAsync(id, ct));

    /// <summary>Resolves the stable, shareable identifier rather than the row id, so a link
    /// pasted into a reply keeps working.</summary>
    [HttpGet("by-slug/{slug}")]
    [Authorize(Permissions.KnowledgeBase.View)]
    public async Task<ActionResult<ArticleDetailDto>> GetBySlug(string slug, CancellationToken ct) =>
        Ok(await articles.GetBySlugAsync(slug, ct));

    [HttpPost]
    [Authorize(Permissions.KnowledgeBase.Manage)]
    public async Task<ActionResult<ArticleDetailDto>> Create(SaveArticleRequest request, CancellationToken ct)
    {
        var created = await articles.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Permissions.KnowledgeBase.Manage)]
    public async Task<ActionResult<ArticleDetailDto>> Update(
        Guid id, SaveArticleRequest request, CancellationToken ct) =>
        Ok(await articles.UpdateAsync(id, request, ct));

    /// <summary>Publishing and archiving are editorial acts, gated separately from editing:
    /// an agent may draft what they learned on a ticket without putting it in front of
    /// customers.</summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Permissions.KnowledgeBase.Publish)]
    public async Task<ActionResult<ArticleDetailDto>> SetStatus(
        Guid id, [FromQuery] ArticleStatus status, CancellationToken ct) =>
        Ok(await articles.SetStatusAsync(id, status, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Permissions.KnowledgeBase.Publish)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await articles.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/versions")]
    [Authorize(Permissions.KnowledgeBase.Manage)]
    public async Task<ActionResult<IReadOnlyList<ArticleVersionDto>>> Versions(Guid id, CancellationToken ct) =>
        Ok(await articles.ListVersionsAsync(id, ct));

    [HttpPost("{id:guid}/versions/{versionId:guid}/restore")]
    [Authorize(Permissions.KnowledgeBase.Manage)]
    public async Task<ActionResult<ArticleDetailDto>> Restore(
        Guid id, Guid versionId, CancellationToken ct) =>
        Ok(await articles.RestoreVersionAsync(id, versionId, ct));

    [HttpPost("{id:guid}/view")]
    [Authorize(Permissions.KnowledgeBase.View)]
    public async Task<IActionResult> RecordView(Guid id, CancellationToken ct)
    {
        await articles.RecordViewAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/vote")]
    [Authorize(Permissions.KnowledgeBase.View)]
    public async Task<ActionResult<ArticleVoteResultDto>> Vote(
        Guid id, ArticleVoteRequest request, CancellationToken ct) =>
        Ok(await articles.VoteAsync(id, request, ct));
}

[ApiController]
[Route("api/kb/categories")]
[Authorize]
public sealed class ArticleCategoriesController(IArticleCategoryService categories) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.KnowledgeBase.View)]
    public async Task<ActionResult<IReadOnlyList<ArticleCategoryDto>>> Tree(
        [FromQuery] bool activeOnly, CancellationToken ct) =>
        Ok(await categories.GetTreeAsync(activeOnly, ct));

    [HttpPost]
    [Authorize(Permissions.KnowledgeBase.Publish)]
    public async Task<ActionResult<ArticleCategoryDto>> Create(
        SaveArticleCategoryRequest request, CancellationToken ct) =>
        Ok(await categories.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Permissions.KnowledgeBase.Publish)]
    public async Task<ActionResult<ArticleCategoryDto>> Update(
        Guid id, SaveArticleCategoryRequest request, CancellationToken ct) =>
        Ok(await categories.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Permissions.KnowledgeBase.Publish)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Articles cited while answering a ticket. Lives under the ticket route because
/// that is where an agent works with it.</summary>
[ApiController]
[Route("api/tickets/{ticketId:guid}/articles")]
[Authorize]
public sealed class TicketArticlesController(IArticleService articles) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<IReadOnlyList<ArticleTicketLinkDto>>> List(
        Guid ticketId, CancellationToken ct) =>
        Ok(await articles.ListTicketLinksAsync(ticketId, ct));

    [HttpPost]
    [Authorize(Permissions.Tickets.Comment)]
    public async Task<ActionResult<ArticleTicketLinkDto>> Link(
        Guid ticketId, LinkArticleToTicketRequest request, CancellationToken ct) =>
        Ok(await articles.LinkToTicketAsync(ticketId, request.ArticleId, ct));

    [HttpDelete("{linkId:guid}")]
    [Authorize(Permissions.Tickets.Comment)]
    public async Task<IActionResult> Unlink(Guid ticketId, Guid linkId, CancellationToken ct)
    {
        await articles.UnlinkFromTicketAsync(ticketId, linkId, ct);
        return NoContent();
    }
}
