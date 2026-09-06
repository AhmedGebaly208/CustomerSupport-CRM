namespace CustomerSupportCRM.Domain.Enums;

/// <summary>Attachments are shared across several owners, so they carry an explicit
/// discriminator rather than one nullable FK per owner type.</summary>
public enum AttachmentOwnerType
{
    Customer = 0,
    Ticket = 1,
    TicketComment = 2,
    Interaction = 3
}
