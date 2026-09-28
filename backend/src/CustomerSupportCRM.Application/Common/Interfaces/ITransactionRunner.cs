namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Runs a unit of work as one atomic transaction.
///
/// Exists because <see cref="IAppDbContext"/> deliberately exposes no EF infrastructure,
/// and because SQL Server is configured with a retrying execution strategy, which refuses
/// user-initiated transactions unless the whole transaction is wrapped so it can be
/// replayed as one retriable unit. Putting that in one place means no caller has to
/// remember it — a lesson from the first time this bit us.
///
/// The delegate may run more than once on a transient failure, so it must reload the state
/// it needs rather than relying on work done before the call.</summary>
public interface ITransactionRunner
{
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);

    Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default);
}
