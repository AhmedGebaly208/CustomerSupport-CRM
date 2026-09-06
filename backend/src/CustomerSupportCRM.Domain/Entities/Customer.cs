using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>Customer profile (area 1). Names are stored per-language rather than in a
/// single field so ar/en listings sort and search correctly in each locale.</summary>
public class Customer : AuditableEntity, IScopedEntity
{
    /// <summary>Human-facing reference (e.g. CUS-000123). Unique, generated on create.</summary>
    public string Code { get; set; } = string.Empty;

    public string FullNameAr { get; set; } = string.Empty;
    public string FullNameEn { get; set; } = string.Empty;

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string? CompanyName { get; set; }
    public string? NationalId { get; set; }
    public string? PreferredLanguage { get; set; } = "ar";
    public string? Address { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    /// <summary>Set when the customer has a portal login (area 8). Null until they register.</summary>
    public Guid? UserId { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();
    public ICollection<CustomerNote> Notes { get; set; } = new List<CustomerNote>();
    public ICollection<Interaction> Interactions { get; set; } = new List<Interaction>();
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
