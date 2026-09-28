using CustomerSupportCRM.Application.Sla;

namespace CustomerSupportCRM.Api.Services;

/// <summary>Runs the SLA sweep on a timer (PDF area 5, "Escalation rules").
///
/// A ticket nobody opens still breaches, so the check cannot live on a request path. The
/// sweep resolves its own scope per pass because the services it uses are scoped, and it
/// swallows failures deliberately: one bad pass must not take the host down and stop every
/// later pass with it.</summary>
public sealed class SlaEvaluatorHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SlaEvaluatorHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);

    /// <summary>Delay before the first sweep, so startup migrations and seeding finish
    /// before the evaluator starts querying.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Sla:EvaluatorEnabled", true))
        {
            logger.LogInformation("SLA evaluator is disabled by configuration.");
            return;
        }

        var interval = configuration.GetValue<TimeSpan?>("Sla:EvaluationInterval") ?? DefaultInterval;

        // A sweep more often than once a minute would spend more time querying than waiting,
        // and the thresholds it checks do not move that fast.
        if (interval < TimeSpan.FromMinutes(1)) interval = TimeSpan.FromMinutes(1);

        logger.LogInformation("SLA evaluator starting; sweeping every {Interval}.", interval);

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
                var evaluator = scope.ServiceProvider.GetRequiredService<ISlaEvaluator>();

                await evaluator.EvaluateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Logged and swallowed: a transient database failure must not end the loop.
                logger.LogError(ex, "SLA sweep failed; the next pass will retry.");
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
