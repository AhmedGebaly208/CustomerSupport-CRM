namespace CustomerSupportCRM.Domain.Enums;

public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,

    /// <summary>Two records folded into one. Recorded explicitly as well as through the
    /// per-row updates, so the trail answers "where did this customer go?" directly.</summary>
    Merged = 3,

    /// <summary>A bulk import. One row per import run, carrying the counts.</summary>
    Imported = 4
}
