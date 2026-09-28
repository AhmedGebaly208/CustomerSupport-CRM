using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Application.Sla.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>SLA policies, their escalation rules, and the sweep that enforces them
/// (PDF area 5).</summary>
[ApiController]
[Route("api/sla/policies")]
[Authorize]
public sealed class SlaPoliciesController(ISlaPolicyService policies, ISlaService sla) : ControllerBase
{
    [HttpGet]
    [Authorize(Permissions.Sla.View)]
    public async Task<ActionResult<IReadOnlyList<SlaPolicyDto>>> List(CancellationToken ct) =>
        Ok(await policies.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [Authorize(Permissions.Sla.View)]
    public async Task<ActionResult<SlaPolicyDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await policies.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Permissions.Sla.Manage)]
    public async Task<ActionResult<SlaPolicyDto>> Create(SaveSlaPolicyRequest request, CancellationToken ct)
    {
        var created = await policies.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Permissions.Sla.Manage)]
    public async Task<ActionResult<SlaPolicyDto>> Update(Guid id, SaveSlaPolicyRequest request, CancellationToken ct) =>
        Ok(await policies.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Permissions.Sla.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await policies.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---- Escalation rules ----

    [HttpPost("{id:guid}/rules")]
    [Authorize(Permissions.Sla.Manage)]
    public async Task<ActionResult<SlaEscalationRuleDto>> AddRule(
        Guid id, SaveSlaEscalationRuleRequest request, CancellationToken ct) =>
        Ok(await policies.AddRuleAsync(id, request, ct));

    [HttpPut("{id:guid}/rules/{ruleId:guid}")]
    [Authorize(Permissions.Sla.Manage)]
    public async Task<ActionResult<SlaEscalationRuleDto>> UpdateRule(
        Guid id, Guid ruleId, SaveSlaEscalationRuleRequest request, CancellationToken ct) =>
        Ok(await policies.UpdateRuleAsync(id, ruleId, request, ct));

    [HttpDelete("{id:guid}/rules/{ruleId:guid}")]
    [Authorize(Permissions.Sla.Manage)]
    public async Task<IActionResult> DeleteRule(Guid id, Guid ruleId, CancellationToken ct)
    {
        await policies.DeleteRuleAsync(id, ruleId, ct);
        return NoContent();
    }

    /// <summary>Shows what a policy would promise for a given priority and start time,
    /// without writing anything — the working-hours arithmetic is hard to predict by eye.</summary>
    [HttpPost("preview")]
    [Authorize(Permissions.Sla.View)]
    public async Task<ActionResult<SlaPreviewDto>> Preview(SlaPreviewRequest request, CancellationToken ct) =>
        Ok(await sla.PreviewAsync(request, ct));
}

[ApiController]
[Route("api/sla")]
[Authorize]
public sealed class SlaController(ISlaService sla, ISlaEvaluator evaluator) : ControllerBase
{
    /// <summary>Where one ticket stands against its targets. Gated on tickets.view rather
    /// than an SLA permission: anyone who can open the ticket can see its clock.</summary>
    [HttpGet("tickets/{ticketId:guid}")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<TicketSlaStatusDto>> GetForTicket(Guid ticketId, CancellationToken ct) =>
        Ok(await sla.GetStatusAsync(ticketId, ct));

    /// <summary>Statuses for a page of tickets in one call, so a list does not issue a
    /// request per row.</summary>
    [HttpPost("tickets")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<IReadOnlyDictionary<Guid, TicketSlaStatusDto>>> GetForTickets(
        [FromBody] IReadOnlyList<Guid> ticketIds, CancellationToken ct) =>
        Ok(await sla.GetStatusesAsync(ticketIds, ct));

    /// <summary>Runs the escalation sweep immediately instead of waiting for the timer.
    /// Useful after changing rules, and for verifying a configuration.</summary>
    [HttpPost("evaluate")]
    [Authorize(Permissions.Sla.Manage)]
    public async Task<ActionResult<object>> Evaluate(CancellationToken ct) =>
        Ok(new { escalated = await evaluator.EvaluateAsync(ct) });
}

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    /// <summary>The caller's own notifications. There is no user id parameter by design —
    /// the recipient is always the signed-in user.</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationListDto>> List(
        [FromQuery] bool unreadOnly = false, [FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await notifications.ListAsync(unreadOnly, take, ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}
