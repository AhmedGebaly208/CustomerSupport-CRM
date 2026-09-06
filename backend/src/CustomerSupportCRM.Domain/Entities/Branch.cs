using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>Multi-branch support (area 12).</summary>
public class Branch : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? City { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
