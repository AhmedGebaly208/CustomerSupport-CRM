using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>The persistence surface Application code is allowed to touch. Infrastructure's
/// DbContext implements it, which keeps EF configuration and Identity out of this layer
/// while still allowing LINQ composition (and InMemory-backed unit tests).</summary>
public interface IAppDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<CustomerContact> CustomerContacts { get; }
    DbSet<CustomerNote> CustomerNotes { get; }
    DbSet<Interaction> Interactions { get; }
    DbSet<Attachment> Attachments { get; }

    DbSet<Ticket> Tickets { get; }
    DbSet<TicketCategory> TicketCategories { get; }
    DbSet<TicketComment> TicketComments { get; }
    DbSet<TicketHistory> TicketHistory { get; }

    DbSet<Tag> Tags { get; }
    DbSet<TicketTag> TicketTags { get; }
    DbSet<TicketWatcher> TicketWatchers { get; }
    DbSet<TicketLink> TicketLinks { get; }
    DbSet<UserSavedView> UserSavedViews { get; }

    DbSet<AiCallLog> AiCallLogs { get; }
    DbSet<AiSuggestionFeedback> AiSuggestionFeedback { get; }

    DbSet<ChannelMessage> ChannelMessages { get; }

    DbSet<ArticleCategory> ArticleCategories { get; }
    DbSet<Article> Articles { get; }
    DbSet<ArticleTag> ArticleTags { get; }
    DbSet<ArticleVersion> ArticleVersions { get; }
    DbSet<ArticleVote> ArticleVotes { get; }
    DbSet<ArticleTicketLink> ArticleTicketLinks { get; }

    DbSet<AgentTask> AgentTasks { get; }
    DbSet<QuickReply> QuickReplies { get; }
    DbSet<TicketMention> TicketMentions { get; }

    DbSet<SlaPolicy> SlaPolicies { get; }
    DbSet<SlaTarget> SlaTargets { get; }
    DbSet<SlaEscalationRule> SlaEscalationRules { get; }
    DbSet<SlaEscalationEvent> SlaEscalationEvents { get; }
    DbSet<Notification> Notifications { get; }

    DbSet<Department> Departments { get; }
    DbSet<Branch> Branches { get; }
    DbSet<AuditLog> AuditLogs { get; }

    // ---- Runtime configuration (area 10) and branding (area 12) ----
    DbSet<BusinessHours> BusinessHours { get; }
    DbSet<Holiday> Holidays { get; }
    DbSet<BrandingSetting> BrandingSettings { get; }
    DbSet<FeatureFlag> FeatureFlags { get; }
    DbSet<ChannelToggle> ChannelToggles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
