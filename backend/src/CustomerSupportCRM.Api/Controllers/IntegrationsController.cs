using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Integrations;
using CustomerSupportCRM.Application.Tickets;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>API keys for external systems (PDF area 11).</summary>
[ApiController]
[Route("api/integrations/api-keys")]
[Authorize(Permissions.SystemConfig.Manage)]
public sealed class ApiKeysController(IApiKeyService keys) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApiKeyDto>>> List(CancellationToken ct) =>
        Ok(await keys.ListAsync(ct));

    /// <summary>Creates a key and returns its plain value once. It is stored only as a hash,
    /// so this response is the only chance to copy it.</summary>
    [HttpPost]
    public async Task<ActionResult<CreatedApiKeyDto>> Create(SaveApiKeyRequest request, CancellationToken ct) =>
        Ok(await keys.CreateAsync(request, ct));

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(
        Guid id, [FromBody] RevokeApiKeyRequest request, CancellationToken ct)
    {
        await keys.RevokeAsync(id, request.Reason, ct);
        return NoContent();
    }

    /// <summary>The scopes a key may be given. The same permission names the UI uses, so an
    /// operator is not inventing strings.</summary>
    [HttpGet("scopes")]
    public ActionResult<IReadOnlyList<string>> Scopes() => Ok(Permissions.All);
}

public sealed record RevokeApiKeyRequest(string? Reason);

/// <summary>Outbound webhooks (PDF area 11).</summary>
[ApiController]
[Route("api/integrations/webhooks")]
[Authorize(Permissions.SystemConfig.Manage)]
public sealed class WebhooksController(IWebhookService webhooks) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WebhookSubscriptionDto>>> List(CancellationToken ct) =>
        Ok(await webhooks.ListAsync(ct));

    /// <summary>Creates a subscription and returns its signing secret once. A receiver needs
    /// it to verify deliveries; it is not returned again.</summary>
    [HttpPost]
    public async Task<ActionResult<CreatedWebhookDto>> Create(SaveWebhookRequest request, CancellationToken ct) =>
        Ok(await webhooks.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WebhookSubscriptionDto>> Update(
        Guid id, SaveWebhookRequest request, CancellationToken ct) =>
        Ok(await webhooks.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await webhooks.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/deliveries")]
    public async Task<ActionResult<IReadOnlyList<WebhookDeliveryDto>>> Deliveries(
        Guid id, CancellationToken ct) =>
        Ok(await webhooks.ListDeliveriesAsync(id, ct));

    /// <summary>Queues a past delivery again, sending the stored payload unchanged.</summary>
    [HttpPost("{id:guid}/deliveries/{deliveryId:guid}/redeliver")]
    public async Task<IActionResult> Redeliver(Guid id, Guid deliveryId, CancellationToken ct)
    {
        await webhooks.RedeliverAsync(id, deliveryId, ct);
        return NoContent();
    }

    [HttpGet("events")]
    public ActionResult<IReadOnlyList<string>> Events() => Ok(WebhookEvents.All);
}

/// <summary>The versioned public API (PDF area 11).
///
/// Versioned in the path from the first release. An external system integrates once and is
/// then outside our release cycle: a breaking change has to arrive as /v2 while /v1 keeps
/// answering, or every integration breaks on a deploy they did not know about.
///
/// It is a deliberately narrow surface over the same services the UI uses, not a second
/// implementation — two implementations of the same rules drift, and the one nobody watches
/// drifts first.</summary>
[ApiController]
[Route("api/v1/public")]
[Authorize]
public sealed class PublicApiV1Controller(ITicketService tickets) : ControllerBase
{
    [HttpGet("tickets")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<PagedResult<TicketListItemDto>>> Tickets(
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] TicketStatus? status,
        [FromQuery] string? search,
        CancellationToken ct) =>
        Ok(await tickets.SearchAsync(
            new TicketQuery
            {
                Page = page <= 0 ? 1 : page,
                // Capped tighter than the UI's: an integration paging through history should
                // not be able to ask for the whole table in one request.
                PageSize = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 200),
                Statuses = status is { } s ? [s] : null,
                Search = search
            }, ct));

    [HttpGet("tickets/{id:guid}")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<TicketDetailDto>> Ticket(Guid id, CancellationToken ct) =>
        Ok(await tickets.GetByIdAsync(id, ct));

    [HttpPost("tickets")]
    [Authorize(Permissions.Tickets.Create)]
    public async Task<ActionResult<TicketDetailDto>> Create(
        CreateTicketRequest request, CancellationToken ct)
    {
        var created = await tickets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Ticket), new { id = created.Id }, created);
    }

    /// <summary>What this version supports, so an integration can check compatibility without
    /// reading a changelog.</summary>
    [HttpGet("meta")]
    [AllowAnonymous]
    public ActionResult<object> Meta() => Ok(new
    {
        version = "v1",
        status = "stable",
        authentication = "X-Api-Key header, or a bearer token",
        events = WebhookEvents.All,
        signature = "HMAC-SHA256 over \"{X-CRM-Timestamp}.{body}\", sent as X-CRM-Signature"
    });
}
