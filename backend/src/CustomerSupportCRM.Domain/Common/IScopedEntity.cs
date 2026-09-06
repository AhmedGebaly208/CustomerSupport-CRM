namespace CustomerSupportCRM.Domain.Common;

/// <summary>Marks an aggregate root that is scoped to a department and branch (PDF area 12,
/// multi-department / multi-branch).
///
/// Only aggregate roots implement this. Child records — ticket comments, ticket history,
/// customer notes, contacts, interactions — are always reached through their parent, and
/// the parent's access check is what gates them. Denormalising the scope onto every child
/// would create two sources of truth that could disagree after a ticket is moved between
/// departments.</summary>
public interface IScopedEntity
{
    Guid? DepartmentId { get; }
    Guid? BranchId { get; }
}
