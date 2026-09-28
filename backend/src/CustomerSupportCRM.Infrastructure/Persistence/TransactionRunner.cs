using CustomerSupportCRM.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Infrastructure.Persistence;

/// <summary>Wraps a unit of work in a transaction that survives the retrying execution
/// strategy configured for SQL Server.
///
/// <c>EnableRetryOnFailure</c> refuses user-initiated transactions unless the whole
/// transaction is wrapped in the strategy, so it can be replayed as a single retriable
/// unit. The change tracker is cleared per attempt so a replay starts from a clean slate
/// rather than from the half-applied state of the failed attempt.</summary>
public sealed class TransactionRunner(AppDbContext db) : ITransactionRunner
{
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var result = await work();
            await transaction.CommitAsync(cancellationToken);

            return result;
        });
    }

    public async Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            await work();
            return 0;
        }, cancellationToken);
}
