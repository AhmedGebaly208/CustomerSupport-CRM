using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.KnowledgeBase;
using CustomerSupportCRM.Application.KnowledgeBase.Dtos;
using CustomerSupportCRM.Application.Portal;
using CustomerSupportCRM.Application.Reports;
using CustomerSupportCRM.Application.Reports.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>The customer-facing portal (PDF area 9).
///
/// Every route is gated on a portal permission that no staff role grants and no staff route
/// accepts, so the two surfaces cannot be reached with the same token by accident. No route
/// here takes a customer id: the service resolves it from the signed-in user, which is what
/// makes horizontal access impossible rather than merely checked.</summary>
[ApiController]
[Route("api/portal")]
[Authorize(Permissions.Portal.Access)]
public sealed class PortalController(IPortalService portal) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<PortalProfileDto>> Profile(CancellationToken ct) =>
        Ok(await portal.GetProfileAsync(ct));

    [HttpGet("tickets")]
    public async Task<ActionResult<PagedResult<PortalTicketListItemDto>>> Tickets(
        [FromQuery] bool openOnly, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct) =>
        Ok(await portal.ListTicketsAsync(
            new PortalTicketQuery(openOnly, page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), ct));

    [HttpGet("tickets/{ticketId:guid}")]
    public async Task<ActionResult<PortalTicketDto>> Ticket(Guid ticketId, CancellationToken ct) =>
        Ok(await portal.GetTicketAsync(ticketId, ct));

    [HttpGet("tickets/{ticketId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<PortalMessageDto>>> Messages(
        Guid ticketId, CancellationToken ct) =>
        Ok(await portal.GetMessagesAsync(ticketId, ct));

    [HttpGet("tickets/{ticketId:guid}/attachments")]
    public async Task<ActionResult<IReadOnlyList<PortalAttachmentDto>>> Attachments(
        Guid ticketId, CancellationToken ct) =>
        Ok(await portal.GetAttachmentsAsync(ticketId, ct));

    [HttpPost("tickets")]
    [Authorize(Permissions.Portal.CreateTicket)]
    public async Task<ActionResult<PortalTicketDto>> Create(
        CreatePortalTicketRequest request, CancellationToken ct)
    {
        var created = await portal.CreateTicketAsync(request, ct);
        return CreatedAtAction(nameof(Ticket), new { ticketId = created.Id }, created);
    }

    [HttpPost("tickets/{ticketId:guid}/replies")]
    [Authorize(Permissions.Portal.Reply)]
    public async Task<ActionResult<PortalMessageDto>> Reply(
        Guid ticketId, PortalReplyRequest request, CancellationToken ct) =>
        Ok(await portal.ReplyAsync(ticketId, request, ct));

    [HttpPost("tickets/{ticketId:guid}/close")]
    [Authorize(Permissions.Portal.Reply)]
    public async Task<ActionResult<PortalTicketDto>> Close(Guid ticketId, CancellationToken ct) =>
        Ok(await portal.CloseTicketAsync(ticketId, ct));

    /// <summary>Rating goes through the reports service, which refuses a ticket that is not
    /// resolved. Ownership is established here first, so a customer cannot rate someone
    /// else's ticket by id.</summary>
    [HttpPost("tickets/{ticketId:guid}/satisfaction")]
    [Authorize(Permissions.Portal.Reply)]
    public async Task<ActionResult<SatisfactionDto>> Rate(
        Guid ticketId,
        SubmitSatisfactionRequest request,
        [FromServices] IReportsService reports,
        CancellationToken ct)
    {
        await portal.GetTicketAsync(ticketId, ct);

        return Ok(await reports.SubmitSatisfactionAsync(ticketId, request, ct));
    }

    /// <summary>Self-service help. Forces the public filter regardless of what the caller
    /// asks for, so an internal article can never be listed here.</summary>
    [HttpGet("articles")]
    public async Task<ActionResult<ArticleSearchResultDto>> Articles(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] bool? isFaq,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromServices] IArticleService articles,
        CancellationToken ct) =>
        Ok(await articles.SearchAsync(
            new ArticleQuery(
                search, categoryId, Tag: null,
                Status: Domain.Enums.ArticleStatus.Published,
                isFaq,
                PublicOnly: true,
                page <= 0 ? 1 : page,
                pageSize <= 0 ? 20 : pageSize),
            ct));
}
