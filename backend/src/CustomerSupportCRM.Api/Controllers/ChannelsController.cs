using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Channels;
using CustomerSupportCRM.Application.Channels.Dtos;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Communication channels (PDF area 3): what is wired up, what arrived, and what
/// went out.</summary>
[ApiController]
[Route("api/channels")]
[Authorize]
public sealed class ChannelsController(
    IChannelRegistry registry,
    IInboundMessageProcessor inbound,
    IOutboundDispatcher outbound,
    IChannelMessageQuery messages) : ControllerBase
{
    /// <summary>Channels this build can speak, and whether each is switched on. The admin
    /// screen needs both: a channel with no adapter cannot usefully be enabled.</summary>
    [HttpGet]
    [Authorize(Permissions.SystemConfig.View)]
    public async Task<ActionResult<IReadOnlyList<ChannelStatusDto>>> Status(CancellationToken ct)
    {
        var result = new List<ChannelStatusDto>();

        foreach (var channel in Enum.GetValues<CommunicationChannel>())
        {
            var available = registry.Available.Contains(channel);

            result.Add(new ChannelStatusDto(
                channel, available, available && await registry.IsEnabledAsync(channel, ct)));
        }

        return Ok(result);
    }

    /// <summary>Accepts a message from a provider.
    ///
    /// Gated on a permission rather than left open: a real provider webhook authenticates
    /// with a shared secret, which the integrations story owns. Until then this is an
    /// authenticated endpoint so nothing can raise tickets anonymously.</summary>
    [HttpPost("inbound")]
    [Authorize(Permissions.Tickets.Create)]
    public async Task<ActionResult<InboundResult>> Inbound(InboundWebhookRequest request, CancellationToken ct)
    {
        var message = new InboundMessage(
            request.Channel,
            string.IsNullOrWhiteSpace(request.ProviderMessageId)
                ? $"api-{Guid.NewGuid():N}"
                : request.ProviderMessageId,
            new InboundParty(
                request.FromName, request.FromEmail, request.FromPhone,
                request.FromWhatsApp, request.FromExternalId),
            request.Body,
            request.ReceivedAt ?? DateTimeOffset.UtcNow,
            request.ConversationId,
            request.Subject,
            Attachments: null,
            Headers: request.Headers);

        return Ok(await inbound.ProcessAsync(message, ct));
    }

    /// <summary>Runs the delivery sweep now instead of waiting for the timer. Useful after
    /// enabling a channel, and for verifying a configuration.</summary>
    [HttpPost("dispatch")]
    [Authorize(Permissions.SystemConfig.Manage)]
    public async Task<ActionResult<object>> Dispatch(CancellationToken ct) =>
        Ok(new { delivered = await outbound.DispatchPendingAsync(ct) });

    /// <summary>The delivery ledger for one ticket, so an agent can see whether their reply
    /// actually left.</summary>
    [HttpGet("messages")]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<IReadOnlyList<ChannelMessageDto>>> Messages(
        [FromQuery] Guid ticketId, CancellationToken ct) =>
        Ok(await messages.ForTicketAsync(ticketId, ct));
}
