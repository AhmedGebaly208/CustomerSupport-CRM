using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>Additional contact details beyond the primary ones on the profile (area 1).</summary>
public class CustomerContact : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public ContactType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool IsPrimary { get; set; }
}
