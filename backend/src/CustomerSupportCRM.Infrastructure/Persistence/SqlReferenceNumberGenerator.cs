using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Tickets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CustomerSupportCRM.Infrastructure.Persistence;

/// <summary>Allocates TKT-/CUS- references from SQL Server SEQUENCE objects.
/// A sequence — rather than MAX(Number) + 1 — is what makes concurrent agents creating
/// tickets at the same moment safe: it is atomic and does not hold a table lock.
/// The sequences are created by the initial migration.</summary>
public sealed class SqlReferenceNumberGenerator(AppDbContext db) : IReferenceNumberGenerator
{
    public const string TicketSequence = "TicketNumberSequence";
    public const string CustomerSequence = "CustomerCodeSequence";

    public async Task<string> NextTicketNumberAsync(CancellationToken cancellationToken = default) =>
        ReferenceNumber.Ticket(await NextValueAsync(TicketSequence, cancellationToken));

    public async Task<string> NextCustomerCodeAsync(CancellationToken cancellationToken = default) =>
        ReferenceNumber.Customer(await NextValueAsync(CustomerSequence, cancellationToken));

    private async Task<long> NextValueAsync(string sequenceName, CancellationToken cancellationToken)
    {
        // The sequence name is a private const, never user input, so interpolating it into
        // the statement cannot be injected into.
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;

        if (shouldClose)
            await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT NEXT VALUE FOR [dbo].[{sequenceName}];";

            if (db.Database.CurrentTransaction is { } transaction)
                command.Transaction = transaction.GetDbTransaction();

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(result);
        }
        catch (SqlException ex) when (ex.Number == 208 || ex.Number == 2812)
        {
            throw new InvalidOperationException(
                $"Sequence [dbo].[{sequenceName}] is missing. Run 'dotnet ef database update' to apply pending migrations.", ex);
        }
        finally
        {
            if (shouldClose)
                await db.Database.CloseConnectionAsync();
        }
    }
}
