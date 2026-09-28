using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class TicketSatisfactionConfiguration : IEntityTypeConfiguration<TicketSatisfaction>
{
    public void Configure(EntityTypeBuilder<TicketSatisfaction> b)
    {
        b.ToTable("TicketSatisfaction");

        b.Property(x => x.Comment).HasMaxLength(2000);

        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        // One rating per ticket, enforced by the database so a double submission corrects
        // rather than duplicates.
        b.HasIndex(x => x.TicketId).IsUnique();

        b.HasIndex(x => x.SubmittedAt);

        // The rating is not soft-deletable but its ticket is. Mirroring the ticket's filter
        // keeps the required end from being filtered away, and a deleted ticket's rating
        // should leave the averages with it.
        b.HasQueryFilter(x => !x.Ticket!.IsDeleted);

        // A score outside the scale would poison every average silently.
        b.ToTable(t => t.HasCheckConstraint("CK_TicketSatisfaction_Score", "[Score] BETWEEN 1 AND 5"));
    }
}
