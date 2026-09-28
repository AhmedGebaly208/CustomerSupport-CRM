using CustomerSupportCRM.Application.Ai;
using CustomerSupportCRM.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>AI assistance (PDF area 7).
///
/// Every endpoint returns a suggestion. None of them changes a ticket: an agent applies what
/// they accept through the ordinary ticket endpoints, which is what keeps the audit trail
/// attributing the change to the person rather than the machine.</summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class AssistanceController(IAssistanceService assistance) : ControllerBase
{
    [HttpPost("tickets/{ticketId:guid}/summary")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<TicketSummaryDto>> Summary(Guid ticketId, CancellationToken ct) =>
        Ok(await assistance.SummariseTicketAsync(ticketId, ct));

    [HttpPost("tickets/{ticketId:guid}/reply")]
    [Authorize(Permissions.Tickets.Comment)]
    public async Task<ActionResult<SuggestedReplyDto>> Reply(Guid ticketId, CancellationToken ct) =>
        Ok(await assistance.SuggestReplyAsync(ticketId, ct));

    [HttpPost("tickets/{ticketId:guid}/category")]
    [Authorize(Permissions.Tickets.Edit)]
    public async Task<ActionResult<CategorySuggestionDto>> Category(Guid ticketId, CancellationToken ct) =>
        Ok(await assistance.SuggestCategoryAsync(ticketId, ct));

    [HttpPost("tickets/{ticketId:guid}/solutions")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<SuggestedSolutionsDto>> Solutions(Guid ticketId, CancellationToken ct) =>
        Ok(await assistance.SuggestSolutionsAsync(ticketId, ct));

    /// <summary>The self-service chatbot. Answers only from published, public articles, and
    /// says when it cannot rather than guessing.</summary>
    [HttpPost("ask")]
    [Authorize(Permissions.KnowledgeBase.View)]
    public async Task<ActionResult<ChatAnswerDto>> Ask(ChatAskRequest request, CancellationToken ct) =>
        Ok(await assistance.AskAsync(request, ct));

    /// <summary>Records whether a suggestion was used. This is how the desk finds out whether
    /// the assistance is worth keeping.</summary>
    [HttpPost("feedback")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<IActionResult> Feedback(AiFeedbackRequest request, CancellationToken ct)
    {
        await assistance.RecordFeedbackAsync(request, ct);
        return NoContent();
    }
}
