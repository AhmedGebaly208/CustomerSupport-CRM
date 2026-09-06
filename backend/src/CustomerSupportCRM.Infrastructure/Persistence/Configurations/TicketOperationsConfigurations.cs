using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> b)
    {
        b.ToTable("Tags");

        b.Property(x => x.Name).IsRequired().HasMaxLength(64);
        b.Property(x => x.ColorHex).HasMaxLength(9);

        // SQL Server's default collation is case-insensitive, so this unique index already
        // makes "VIP" and "vip" the same tag without a computed column.
        b.HasIndex(x => x.Name).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class TicketTagConfiguration : IEntityTypeConfiguration<TicketTag>
{
    public void Configure(EntityTypeBuilder<TicketTag> b)
    {
        b.ToTable("TicketTags");

        b.HasOne(x => x.Ticket).WithMany(x => x.TicketTags)
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Tag).WithMany(x => x.TicketTags)
            .HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);

        // One row per (ticket, tag). Filtered so a soft-deleted row does not block re-tagging.
        b.HasIndex(x => new { x.TicketId, x.TagId }).IsUnique().HasFilter("[IsDeleted] = 0");

        // Drives the filter-by-tag query on the ticket list.
        b.HasIndex(x => new { x.TagId, x.TicketId });
    }
}

public sealed class TicketWatcherConfiguration : IEntityTypeConfiguration<TicketWatcher>
{
    public void Configure(EntityTypeBuilder<TicketWatcher> b)
    {
        b.ToTable("TicketWatchers");

        b.HasOne(x => x.Ticket).WithMany(x => x.Watchers)
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.TicketId, x.UserId }).IsUnique().HasFilter("[IsDeleted] = 0");

        // "Tickets I watch" and the SLA notifier both query from the user side.
        b.HasIndex(x => x.UserId);
    }
}

public sealed class TicketLinkConfiguration : IEntityTypeConfiguration<TicketLink>
{
    public void Configure(EntityTypeBuilder<TicketLink> b)
    {
        b.ToTable("TicketLinks");

        // NoAction on both ends: two cascade paths into the same table is a SQL Server
        // error, and a link is metadata that should not silently delete a ticket anyway.
        b.HasOne(x => x.SourceTicket).WithMany(x => x.OutgoingLinks)
            .HasForeignKey(x => x.SourceTicketId).OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.TargetTicket).WithMany(x => x.IncomingLinks)
            .HasForeignKey(x => x.TargetTicketId).OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(x => new { x.SourceTicketId, x.TargetTicketId, x.Type })
            .IsUnique().HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => x.TargetTicketId);
    }
}

public sealed class UserSavedViewConfiguration : IEntityTypeConfiguration<UserSavedView>
{
    public void Configure(EntityTypeBuilder<UserSavedView> b)
    {
        b.ToTable("UserSavedViews");

        b.Property(x => x.Name).IsRequired().HasMaxLength(64);
        b.Property(x => x.EntityKind).IsRequired().HasMaxLength(32);

        // The blob is opaque to the server; the cap is what stops it becoming a dumping
        // ground. Validated as well-formed JSON in the validator.
        b.Property(x => x.FiltersJson).IsRequired().HasMaxLength(8000);

        b.HasIndex(x => new { x.UserId, x.EntityKind, x.Name })
            .IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
