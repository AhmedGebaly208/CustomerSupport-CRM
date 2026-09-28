using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Workspace;
using CustomerSupportCRM.Application.Workspace.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>The agent's own workspace (PDF area 4): board, tasks, reminders and saved
/// replies.</summary>
[ApiController]
[Route("api/workspace")]
[Authorize]
public sealed class WorkspaceController(IWorkspaceService workspace) : ControllerBase
{
    /// <summary>Everything the signed-in agent's board shows, in one request. There is no
    /// agent id parameter — the board is always the caller's own.</summary>
    [HttpGet("me")]
    [Authorize(Permissions.Dashboard.View)]
    public async Task<ActionResult<AgentWorkspaceDto>> Mine(CancellationToken ct) =>
        Ok(await workspace.GetMyWorkspaceAsync(ct));

    [HttpGet("team")]
    [Authorize(Permissions.Dashboard.ViewTeam)]
    public async Task<ActionResult<TeamDashboardDto>> Team(
        [FromQuery] Guid? departmentId, CancellationToken ct) =>
        Ok(await workspace.GetTeamDashboardAsync(departmentId, ct));

    // ---- Tasks ----

    [HttpGet("tasks")]
    [Authorize(Permissions.Dashboard.View)]
    public async Task<ActionResult<IReadOnlyList<AgentTaskDto>>> Tasks(
        [FromQuery] bool includeDone, [FromQuery] DateTimeOffset? dueBefore,
        [FromQuery] int take, CancellationToken ct) =>
        Ok(await workspace.ListTasksAsync(new AgentTaskQuery(includeDone, dueBefore, take == 0 ? 50 : take), ct));

    [HttpPost("tasks")]
    [Authorize(Permissions.Dashboard.View)]
    public async Task<ActionResult<AgentTaskDto>> CreateTask(SaveAgentTaskRequest request, CancellationToken ct) =>
        Ok(await workspace.CreateTaskAsync(request, ct));

    [HttpPut("tasks/{id:guid}")]
    [Authorize(Permissions.Dashboard.View)]
    public async Task<ActionResult<AgentTaskDto>> UpdateTask(
        Guid id, SaveAgentTaskRequest request, CancellationToken ct) =>
        Ok(await workspace.UpdateTaskAsync(id, request, ct));

    [HttpPost("tasks/{id:guid}/done")]
    [Authorize(Permissions.Dashboard.View)]
    public async Task<ActionResult<AgentTaskDto>> SetTaskDone(
        Guid id, [FromQuery] bool done, CancellationToken ct) =>
        Ok(await workspace.SetTaskDoneAsync(id, done, ct));

    [HttpDelete("tasks/{id:guid}")]
    [Authorize(Permissions.Dashboard.View)]
    public async Task<IActionResult> DeleteTask(Guid id, CancellationToken ct)
    {
        await workspace.DeleteTaskAsync(id, ct);
        return NoContent();
    }

    // ---- Quick replies ----

    [HttpGet("quick-replies")]
    [Authorize(Permissions.Tickets.Comment)]
    public async Task<ActionResult<IReadOnlyList<QuickReplyDto>>> QuickReplies(CancellationToken ct) =>
        Ok(await workspace.ListQuickRepliesAsync(ct));

    [HttpPost("quick-replies")]
    [Authorize(Permissions.Lookups.Manage)]
    public async Task<ActionResult<QuickReplyDto>> CreateQuickReply(
        SaveQuickReplyRequest request, CancellationToken ct) =>
        Ok(await workspace.CreateQuickReplyAsync(request, ct));

    [HttpPut("quick-replies/{id:guid}")]
    [Authorize(Permissions.Lookups.Manage)]
    public async Task<ActionResult<QuickReplyDto>> UpdateQuickReply(
        Guid id, SaveQuickReplyRequest request, CancellationToken ct) =>
        Ok(await workspace.UpdateQuickReplyAsync(id, request, ct));

    [HttpDelete("quick-replies/{id:guid}")]
    [Authorize(Permissions.Lookups.Manage)]
    public async Task<IActionResult> DeleteQuickReply(Guid id, CancellationToken ct)
    {
        await workspace.DeleteQuickReplyAsync(id, ct);
        return NoContent();
    }
}
