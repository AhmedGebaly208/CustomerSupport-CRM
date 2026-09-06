using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");

        b.Property(x => x.Code).IsRequired().HasMaxLength(32);
        b.Property(x => x.FullNameAr).IsRequired().HasMaxLength(200);
        b.Property(x => x.FullNameEn).IsRequired().HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(32);
        b.Property(x => x.WhatsAppNumber).HasMaxLength(32);
        b.Property(x => x.CompanyName).HasMaxLength(200);
        b.Property(x => x.NationalId).HasMaxLength(32);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.PreferredLanguage).HasMaxLength(8);

        // Filtered so soft-deleted rows do not block a code being reissued.
        b.HasIndex(x => x.Code).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.Email);
        b.HasIndex(x => x.Phone);
        b.HasIndex(x => new { x.DepartmentId, x.BranchId });

        b.HasOne(x => x.Department).WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Branch).WithMany()
            .HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> b)
    {
        b.ToTable("CustomerContacts");

        b.Property(x => x.Value).IsRequired().HasMaxLength(256);
        b.Property(x => x.Label).HasMaxLength(100);

        b.HasOne(x => x.Customer).WithMany(x => x.Contacts)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.CustomerId);
    }
}

public sealed class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> b)
    {
        b.ToTable("CustomerNotes");

        b.Property(x => x.Body).IsRequired().HasMaxLength(4000);

        b.HasOne(x => x.Customer).WithMany(x => x.Notes)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.CustomerId, x.CreatedAt });
    }
}

public sealed class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    public void Configure(EntityTypeBuilder<Interaction> b)
    {
        b.ToTable("Interactions");

        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.Body).IsRequired().HasMaxLength(8000);

        b.HasOne(x => x.Customer).WithMany(x => x.Interactions)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);

        // A ticket is deleted independently of the touchpoints it generated, so the
        // interaction survives with a null TicketId rather than cascading away.
        b.HasOne(x => x.Ticket).WithMany(x => x.Interactions)
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.CustomerId, x.OccurredAt });
        b.HasIndex(x => x.TicketId);
    }
}

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("Attachments");

        b.Property(x => x.FileName).IsRequired().HasMaxLength(300);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(150);
        b.Property(x => x.StoragePath).IsRequired().HasMaxLength(500);

        // Polymorphic owner, so the lookup index covers both discriminator columns.
        b.HasIndex(x => new { x.OwnerType, x.OwnerId });
    }
}

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("Departments");

        b.Property(x => x.Code).IsRequired().HasMaxLength(32);
        b.Property(x => x.NameAr).IsRequired().HasMaxLength(150);
        b.Property(x => x.NameEn).IsRequired().HasMaxLength(150);

        b.HasIndex(x => x.Code).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("Branches");

        b.Property(x => x.Code).IsRequired().HasMaxLength(32);
        b.Property(x => x.NameAr).IsRequired().HasMaxLength(150);
        b.Property(x => x.NameEn).IsRequired().HasMaxLength(150);
        b.Property(x => x.City).HasMaxLength(100);

        b.HasIndex(x => x.Code).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");

        b.Property(x => x.EntityName).IsRequired().HasMaxLength(150);
        // Composite keys are serialised as comma-joined values (Identity's UserRoles is
        // two GUIDs = 73 chars), so this must be well clear of a single GUID's length.
        b.Property(x => x.EntityId).IsRequired().HasMaxLength(256);
        b.Property(x => x.UserName).HasMaxLength(256);
        b.Property(x => x.IpAddress).HasMaxLength(64);

        b.HasIndex(x => new { x.EntityName, x.EntityId });
        b.HasIndex(x => x.OccurredAt);

        // Covers the default browse query (newest first, optionally narrowed by entity and
        // action) without touching the base table.
        b.HasIndex(x => new { x.OccurredAt, x.EntityName, x.Action })
            .HasDatabaseName("IX_AuditLogs_Browse");

        // Filtered: most rows carry a user, but system-initiated writes do not, and there is
        // no point indexing those.
        b.HasIndex(x => x.UserId)
            .HasFilter("[UserId] IS NOT NULL");
    }
}
