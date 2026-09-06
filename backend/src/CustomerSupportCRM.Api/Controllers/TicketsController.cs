using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Tickets;
using CustomerSupportCRM.Application.Tickets.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Ticket management (PDF area 2) and the agent dashboard (area 4).
///
/// Authorised by permission, not role. Previous role mapping, preserved exactly:
/// Staff (Admin/Manager/Agent) had full read+write and saw internal notes; Supervisory
/// (Admin/Manager) alone could delete and view another agent's dashboard.</summary>
[ApiController]
[Route("api/tickets")]
[Authorize(Policy = Permissions.Tickets.View)]
public sealed class TicketsController(ITicketService tickets, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketListItemDto>>> Search(
        [FromQuery] TicketQuery query, CancellationToken ct) =>
        Ok(await tickets.SearchAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TicketDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailDto>> GetById(Guid id, CancellationToken ct) =>
        Ok(await tickets.GetByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Permissions.Tickets.Create)]
    [ProducesResponseType<TicketDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketDetailDto>> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var created = await tickets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Tickets.Edit)]
    [ProducesResponseType<TicketDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketDetailDto>> Update(Guid id, UpdateTicketRequest request, CancellationToken ct) =>
        Ok(await tickets.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Tickets.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tickets.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Assigns or unassigns the ticket. Pass a null agentId to return it to the queue.</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = Permissions.Tickets.Assign)]
    [ProducesResponseType<TicketDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailDto>> Assign(Guid id, AssignTicketRequest request, CancellationToken ct) =>
        Ok(await tickets.AssignAsync(id, request, ct));

    /// <summary>Moves the ticket to a new status. Rejected with 409 when the workflow
    /// does not allow the transition.</summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = Permissions.Tickets.Close)]
    [ProducesResponseType<TicketDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketDetailDto>> ChangeStatus(
        Guid id, ChangeTicketStatusRequest request, CancellationToken ct) =>
        Ok(await tickets.ChangeStatusAsync(id, request, ct));

    // ---- Comments ----

    [HttpGet("{id:guid}/comments")]
    [ProducesResponseType<IReadOnlyList<TicketCommentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketCommentDto>>> GetComments(Guid id, CancellationToken ct)
    {
        // Internal notes are staff-only; a portal customer hitting this shared route must
        // never see them. One permission replaces the previous three role checks, so the
        // portal story cannot accidentally grant it by adding a role.
        var includeInternal = currentUser.HasPermission(Permissions.Tickets.ViewInternal);

        return Ok(await tickets.GetCommentsAsync(id, includeInternal, ct));
    }

    [HttpPost("{id:guid}/comments")]
    [Authorize(Policy = Permissions.Tickets.Comment)]
    [ProducesResponseType<TicketCommentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketCommentDto>> AddComment(
        Guid id, CreateTicketCommentRequest request, CancellationToken ct)
    {
        var comment = await tickets.AddCommentAsync(id, request, ct);
        return CreatedAtAction(nameof(GetComments), new { id }, comment);
    }

    // ---- History ----

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType<IReadOnlyList<TicketHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TicketHistoryDto>>> GetHistory(Guid id, CancellationToken ct) =>
        Ok(await tickets.GetHistoryAsync(id, ct));

    // ---- Agent dashboard (area 4) ----

    /// <summary>Dashboard for the signed-in agent, or for another agent when a supervisor asks.</summary>
    [HttpGet("/api/dashboard/agent")]
    [Authorize(Policy = Permissions.Dashboard.View)]
    [ProducesResponseType<AgentDashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AgentDashboardDto>> AgentDashboard(
        [FromQuery] Guid? agentId, CancellationToken ct)
    {
        var callerId = currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

        // Only holders of the team-view permission may look at another agent's board.
        if (agentId is { } requested && requested != callerId
            && !currentUser.HasPermission(Permissions.Dashboard.ViewTeam))
        {
            throw new ForbiddenException("You can only view your own dashboard.");
        }

        return Ok(await tickets.GetAgentDashboardAsync(agentId ?? callerId, ct));
    }
}
