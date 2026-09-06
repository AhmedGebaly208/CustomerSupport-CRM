using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Enums;

namespace CustomerSupportCRM.Domain.Entities;

/// <summary>File attached to a customer, ticket, comment or interaction (areas 1 and 2).
/// Bytes live on disk under the API's upload root; only metadata is stored here.</summary>
public class Attachment : AuditableEntity
{
    public AttachmentOwnerType OwnerType { get; set; }
    public Guid OwnerId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Path relative to the configured storage root — never an absolute path,
    /// so the storage root can move between environments.</summary>
    public string StoragePath { get; set; } = string.Empty;
}
