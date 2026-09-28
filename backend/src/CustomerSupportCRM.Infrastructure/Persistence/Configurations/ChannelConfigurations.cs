using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class ChannelMessageConfiguration : IEntityTypeConfiguration<ChannelMessage>
{
    public void Configure(EntityTypeBuilder<ChannelMessage> b)
    {
        b.ToTable("ChannelMessages");

        b.Property(x => x.ProviderMessageId).HasMaxLength(256);
        b.Property(x => x.ProviderConversationId).HasMaxLength(256);
        b.Property(x => x.Address).HasMaxLength(256);
        b.Property(x => x.Subject).HasMaxLength(300);
        b.Property(x => x.LastError).HasMaxLength(1000);

        // SetNull rather than Cascade: the delivery ledger outlives the ticket it belonged
        // to, because "did this reply ever reach the customer?" is a question worth being
        // able to answer after the ticket is gone.
        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);

        // The idempotency guarantee. Filtered because outbound rows have no provider id
        // until the send succeeds, and many nulls would collide.
        b.HasIndex(x => new { x.Channel, x.ProviderMessageId })
            .IsUnique()
            .HasFilter("[ProviderMessageId] IS NOT NULL");

        // The dispatcher's sweep: what is waiting, and what is due.
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });

        b.HasIndex(x => x.TicketId);
        b.HasIndex(x => new { x.Channel, x.ProviderConversationId });
    }
}
