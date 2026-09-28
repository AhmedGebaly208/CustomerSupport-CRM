using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> b)
    {
        b.ToTable("AgentTasks");

        b.Property(x => x.Title).IsRequired().HasMaxLength(300);
        b.Property(x => x.Notes).HasMaxLength(4000);

        // SetNull, not Cascade: deleting a ticket must not take an agent's own reminder with
        // it. The task survives with its link cleared.
        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);

        // The board asks for one owner's open tasks in due order on every load.
        b.HasIndex(x => new { x.OwnerUserId, x.IsDone, x.DueAt });

        // The dispatcher asks for reminders that are due and not yet sent.
        b.HasIndex(x => new { x.IsReminder, x.ReminderSentAt, x.DueAt });
    }
}

public sealed class QuickReplyConfiguration : IEntityTypeConfiguration<QuickReply>
{
    public void Configure(EntityTypeBuilder<QuickReply> b)
    {
        b.ToTable("QuickReplies");

        b.Property(x => x.TitleAr).IsRequired().HasMaxLength(200);
        b.Property(x => x.TitleEn).IsRequired().HasMaxLength(200);
        b.Property(x => x.BodyAr).IsRequired().HasMaxLength(4000);
        b.Property(x => x.BodyEn).IsRequired().HasMaxLength(4000);
        b.Property(x => x.Shortcut).HasMaxLength(50);

        // Filtered so the many snippets without a shortcut do not collide on null.
        b.HasIndex(x => x.Shortcut)
            .IsUnique()
            .HasFilter("[Shortcut] IS NOT NULL AND [IsDeleted] = 0");

        b.HasIndex(x => new { x.IsActive, x.DepartmentId });
    }
}

public sealed class TicketMentionConfiguration : IEntityTypeConfiguration<TicketMention>
{
    public void Configure(EntityTypeBuilder<TicketMention> b)
    {
        b.ToTable("TicketMentions");

        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        // NoAction on the second path to the same ticket: SQL Server rejects more than one
        // cascade route into a table, and the comment cascade already removes these rows.
        b.HasOne(x => x.Comment).WithMany()
            .HasForeignKey(x => x.TicketCommentId).OnDelete(DeleteBehavior.NoAction);

        // "Tickets where I was mentioned", newest first.
        b.HasIndex(x => new { x.MentionedUserId, x.MentionedAt });

        // One mention per person per comment, so editing and re-saving cannot double-notify.
        b.HasIndex(x => new { x.TicketCommentId, x.MentionedUserId }).IsUnique();

        // The mention is append-only and so not soft-deletable, but its comment is. Mirroring
        // the comment's filter keeps the required end from being filtered away — a mention on
        // a deleted comment should disappear with it, exactly as TicketHistory does.
        b.HasQueryFilter(x => !x.Comment!.IsDeleted && !x.Ticket!.IsDeleted);
    }
}
