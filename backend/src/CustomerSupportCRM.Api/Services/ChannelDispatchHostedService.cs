using CustomerSupportCRM.Application.Channels;

namespace CustomerSupportCRM.Api.Services;

/// <summary>Delivers queued outbound messages and retries the ones that failed
/// transiently (PDF area 3).
///
/// Separate from the SLA and reminder sweeps: this one has to run often, because a customer
/// waiting on a reply notices a delay that an escalation check would not.</summary>
public sealed class ChannelDispatchHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ChannelDispatchHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Channels:DispatchEnabled", true))
        {
            logger.LogInformation("Channel dispatch is disabled by configuration.");
            return;
        }

        var interval = configuration.GetValue<TimeSpan?>("Channels:DispatchInterval") ?? DefaultInterval;
        if (interval < TimeSpan.FromSeconds(5)) interval = TimeSpan.FromSeconds(5);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboundDispatcher>();

                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Swallowed so one bad pass does not end delivery for the whole process.
                logger.LogError(ex, "Channel dispatch failed; the next pass will retry.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
