using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>Free-text note on a customer profile (area 1).</summary>
public class CustomerNote : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Internal notes are never exposed through the customer portal.</summary>
    public bool IsInternal { get; set; } = true;
}
