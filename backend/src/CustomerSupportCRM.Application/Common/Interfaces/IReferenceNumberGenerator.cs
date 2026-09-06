namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Allocates the next TKT-/CUS- reference. Backed by a SQL Server SEQUENCE in
/// Infrastructure so concurrent agents can never be handed the same number; tests supply
/// an in-memory counter.</summary>
public interface IReferenceNumberGenerator
{
    Task<string> NextTicketNumberAsync(CancellationToken cancellationToken = default);
    Task<string> NextCustomerCodeAsync(CancellationToken cancellationToken = default);
}
