using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class AiCallLogConfiguration : IEntityTypeConfiguration<AiCallLog>
{
    public void Configure(EntityTypeBuilder<AiCallLog> b)
    {
        b.ToTable("AiCallLogs");

        b.Property(x => x.Provider).HasMaxLength(100);
        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.Error).HasMaxLength(1000);

        // SetNull, not Cascade: the cost record outlives the ticket, because a question
        // about last quarter's spend must still answer after tickets are cleaned up.
        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.OccurredAt, x.Feature });
    }
}

public sealed class AiSuggestionFeedbackConfiguration : IEntityTypeConfiguration<AiSuggestionFeedback>
{
    public void Configure(EntityTypeBuilder<AiSuggestionFeedback> b)
    {
        b.ToTable("AiSuggestionFeedback");

        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.SetNull);

        // The question this table exists to answer: is a feature being accepted or rejected.
        b.HasIndex(x => new { x.Feature, x.Outcome, x.OccurredAt });
    }
}
