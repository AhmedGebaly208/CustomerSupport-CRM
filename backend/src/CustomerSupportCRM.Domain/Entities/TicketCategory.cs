using CustomerSupportCRM.Domain.Common;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>Ticket category tree (area 2). Self-referencing so departments can nest
/// sub-categories without a schema change.</summary>
public class TicketCategory : AuditableEntity, INamedLookup
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }
    public TicketCategory? Parent { get; set; }
    public ICollection<TicketCategory> Children { get; set; } = new List<TicketCategory>();

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
