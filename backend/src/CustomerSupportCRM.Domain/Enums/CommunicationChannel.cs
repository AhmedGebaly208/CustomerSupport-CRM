namespace CustomerSupportCRM.Domain.Enums;

/// <summary>Channels a ticket or interaction can originate from (area 3).
/// The bootstrap records the channel; the per-channel integrations arrive with
/// the `channels` story.</summary>
public enum CommunicationChannel
{
    Email = 0,
    WhatsApp = 1,
    LiveChat = 2,
    Sms = 3,
    WebForm = 4,
    Portal = 5,
    Phone = 6,
    Internal = 7
}
