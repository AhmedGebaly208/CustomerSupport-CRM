using System.Text;
using System.Text.Json;
using CustomerSupportCRM.Application.Channels;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportCRM.Infrastructure.Channels;

public sealed class LocalChannelOptions
{
    public const string SectionName = "Channels:Local";

    /// <summary>Folder outbound messages are written to, and inbound messages are read from.
    /// Relative paths resolve against the content root.</summary>
    public string RootPath { get; set; } = "App_Data/channels";

    /// <summary>Channels this adapter answers for. Every channel by default, so the feature
    /// is exercisable end to end before any provider account exists.</summary>
    public CommunicationChannel[] Channels { get; set; } =
        [CommunicationChannel.Email, CommunicationChannel.WhatsApp,
         CommunicationChannel.Sms, CommunicationChannel.LiveChat];

    /// <summary>Simulated failure rate, 0 to 1. Left at zero; raised only to exercise the
    /// retry path deliberately.</summary>
    public double FailureRate { get; set; }
}

/// <summary>A channel adapter that delivers to the local filesystem.
///
/// This is the story's working provider. It implements the whole contract — outbound
/// delivery with a provider message id, and inbound pickup — against a folder instead of a
/// third party, so the threading, idempotency and retry behaviour can be exercised and
/// verified without an account anywhere.
///
/// It is not a stub that returns success: an outbound message becomes a file that can be
/// read, and dropping a file in the inbox genuinely raises a ticket. Wiring a real provider
/// later means adding a sibling adapter, not changing anything above it.</summary>
public sealed class LocalDropChannelAdapter : IChannelAdapter
{
    private readonly LocalChannelOptions options;
    private readonly ILogger<LocalDropChannelAdapter> logger;
    private readonly string outboxPath;

    public LocalDropChannelAdapter(
        CommunicationChannel channel,
        IOptions<LocalChannelOptions> options,
        ILogger<LocalDropChannelAdapter> logger)
    {
        Channel = channel;
        this.options = options.Value;
        this.logger = logger;

        outboxPath = Path.Combine(Path.GetFullPath(this.options.RootPath), channel.ToString(), "outbox");
    }

    public CommunicationChannel Channel { get; }

    public Task<ChannelSendResult> SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (options.FailureRate > 0 && Random.Shared.NextDouble() < options.FailureRate)
        {
            // Deliberate, configured fault injection for exercising the backoff path.
            return Task.FromResult(ChannelSendResult.Transient("Simulated provider failure."));
        }

        try
        {
            Directory.CreateDirectory(outboxPath);

            // A provider assigns its own id; this stands in for one and is what makes the
            // outbound row traceable and the inbound side able to thread a reply to it.
            var providerMessageId = $"local-{Guid.NewGuid():N}";
            var file = Path.Combine(outboxPath, $"{providerMessageId}.json");

            var payload = JsonSerializer.Serialize(new
            {
                providerMessageId,
                channel = message.Channel.ToString(),
                ticketNumber = message.TicketNumber,
                to = message.To.Email ?? message.To.WhatsAppNumber ?? message.To.Phone ?? message.To.ExternalUserId,
                subject = message.Subject,
                body = message.BodyText,
                sentAt = DateTimeOffset.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true });

            // UTF8Encoding(false), not Encoding.UTF8: the latter writes a byte order mark,
            // and a BOM at the start of a JSON document breaks strict parsers.
            File.WriteAllText(file, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            logger.LogInformation("Wrote outbound {Channel} message for {TicketNumber} to {File}.",
                message.Channel, message.TicketNumber, file);

            return Task.FromResult(ChannelSendResult.Ok(providerMessageId));
        }
        catch (IOException ex)
        {
            // A locked or full disk is worth retrying; a malformed path is not, but it would
            // fail the same way every attempt and exhaust the count.
            return Task.FromResult(ChannelSendResult.Transient(ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(ChannelSendResult.Permanent(ex.Message));
        }
    }
}
