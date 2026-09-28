using CustomerSupportCRM.Application.Workspace;

namespace CustomerSupportCRM.Api.Services;

/// <summary>Fires agents' reminders on a timer (PDF area 4).
///
/// Separate from the SLA sweep despite the similar shape: reminders are personal and cheap
/// to check, SLA evaluation is desk-wide and heavier, and tying them together would force
/// one cadence on both.</summary>
public sealed class ReminderHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ReminderHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Workspace:RemindersEnabled", true))
        {
            logger.LogInformation("Reminder dispatcher is disabled by configuration.");
            return;
        }

        var interval = configuration.GetValue<TimeSpan?>("Workspace:ReminderInterval") ?? DefaultInterval;
        if (interval < TimeSpan.FromSeconds(30)) interval = TimeSpan.FromSeconds(30);

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
                var dispatcher = scope.ServiceProvider.GetRequiredService<IReminderDispatcher>();

                await dispatcher.DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Logged and swallowed: one bad pass must not end the loop.
                logger.LogError(ex, "Reminder dispatch failed; the next pass will retry.");
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
