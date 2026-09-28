using CustomerSupportCRM.Application.Integrations;

namespace CustomerSupportCRM.Api.Services;

/// <summary>Delivers queued webhook events on a timer (PDF area 11).</summary>
public sealed class WebhookDispatchHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<WebhookDispatchHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(25);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Integrations:WebhooksEnabled", true))
        {
            logger.LogInformation("Webhook delivery is disabled by configuration.");
            return;
        }

        var interval = configuration.GetValue<TimeSpan?>("Integrations:WebhookInterval") ?? DefaultInterval;
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
                var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Swallowed so one bad pass does not end delivery for the process.
                logger.LogError(ex, "Webhook dispatch failed; the next pass will retry.");
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
