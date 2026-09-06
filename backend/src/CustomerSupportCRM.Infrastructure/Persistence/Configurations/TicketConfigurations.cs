using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> b)
    {
        b.ToTable("Tickets");

        b.Property(x => x.Number).IsRequired().HasMaxLength(32);
        b.Property(x => x.Subject).IsRequired().HasMaxLength(300);
        b.Property(x => x.Description).IsRequired().HasMaxLength(8000);

        b.HasIndex(x => x.Number).IsUnique().HasFilter("[IsDeleted] = 0");

        // Covers the agent dashboard and the default queue view, which always filter
        // on assignee + status and sort by priority.
        b.HasIndex(x => new { x.AssignedAgentId, x.Status, x.Priority });
        b.HasIndex(x => new { x.Status, x.Priority });
        b.HasIndex(x => x.CustomerId);
        b.HasIndex(x => new { x.DepartmentId, x.Status });
        b.HasIndex(x => x.ResolutionDueAt);

        b.HasOne(x => x.Customer).WithMany(x => x.Tickets)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Category).WithMany(x => x.Tickets)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Department).WithMany(x => x.Tickets)
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Branch).WithMany(x => x.Tickets)
            .HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class TicketCategoryConfiguration : IEntityTypeConfiguration<TicketCategory>
{
    public void Configure(EntityTypeBuilder<TicketCategory> b)
    {
        b.ToTable("TicketCategories");

        b.Property(x => x.NameAr).IsRequired().HasMaxLength(150);
        b.Property(x => x.NameEn).IsRequired().HasMaxLength(150);

        // Restrict, not Cascade: SQL Server rejects cascade paths on a self-reference,
        // and silently deleting a whole sub-tree would be wrong anyway.
        b.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Department).WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.ParentId, x.SortOrder });
    }
}

public sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> b)
    {
        b.ToTable("TicketComments");

        b.Property(x => x.Body).IsRequired().HasMaxLength(8000);

        b.HasOne(x => x.Ticket).WithMany(x => x.Comments)
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.TicketId, x.CreatedAt });
    }
}

public sealed class TicketHistoryConfiguration : IEntityTypeConfiguration<TicketHistory>
{
    public void Configure(EntityTypeBuilder<TicketHistory> b)
    {
        b.ToTable("TicketHistory");

        b.Property(x => x.Field).IsRequired().HasMaxLength(100);
        b.Property(x => x.OldValue).HasMaxLength(500);
        b.Property(x => x.NewValue).HasMaxLength(500);
        b.Property(x => x.Note).HasMaxLength(1000);

        b.HasOne(x => x.Ticket).WithMany(x => x.History)
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.TicketId, x.ChangedAt });

        // TicketHistory is append-only and so not ISoftDeletable, but its parent Ticket is.
        // Without a matching filter EF warns that history of a soft-deleted ticket stays
        // visible through this required relationship.
        b.HasQueryFilter(x => !x.Ticket!.IsDeleted);
    }
}
