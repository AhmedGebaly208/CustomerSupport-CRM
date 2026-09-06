namespace CustomerSupportCRM.Domain.Tickets;

/// <summary>Reference-number formatting. The sequence itself comes from the database;
/// this only owns the shape so every caller produces identical strings.</summary>
public static class ReferenceNumber
{
    public const string TicketPrefix = "TKT";
    public const string CustomerPrefix = "CUS";

    public static string For(string prefix, long sequence) => $"{prefix}-{sequence:D6}";

    public static string Ticket(long sequence) => For(TicketPrefix, sequence);

    public static string Customer(long sequence) => For(CustomerPrefix, sequence);
}
