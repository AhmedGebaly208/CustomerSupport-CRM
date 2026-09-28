using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> b)
    {
        b.ToTable("SlaPolicies");

        b.Property(x => x.NameAr).IsRequired().HasMaxLength(200);
        b.Property(x => x.NameEn).IsRequired().HasMaxLength(200);
        b.Property(x => x.PausedStatuses).IsRequired().HasMaxLength(200);

        // Specificity is derived from the criteria columns, so it is computed in memory and
        // never stored — a stored copy could disagree with the columns it summarises.
        b.Ignore(x => x.Specificity);

        // Restrict, not cascade: deleting a department must not silently take the SLA
        // policies of every ticket that referenced it.
        b.HasOne(x => x.Department).WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Branch).WithMany()
            .HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Category).WithMany()
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // The resolver filters on these three and then orders by specificity and rank.
        b.HasIndex(x => new { x.IsActive, x.CategoryId, x.DepartmentId, x.BranchId });
    }
}

public sealed class SlaTargetConfiguration : IEntityTypeConfiguration<SlaTarget>
{
    public void Configure(EntityTypeBuilder<SlaTarget> b)
    {
        b.ToTable("SlaTargets");

        b.HasOne(x => x.Policy).WithMany(x => x.Targets)
            .HasForeignKey(x => x.SlaPolicyId).OnDelete(DeleteBehavior.Cascade);

        // One target per priority per policy; a second would make resolution ambiguous.
        b.HasIndex(x => new { x.SlaPolicyId, x.Priority }).IsUnique();
    }
}

public sealed class SlaEscalationRuleConfiguration : IEntityTypeConfiguration<SlaEscalationRule>
{
    public void Configure(EntityTypeBuilder<SlaEscalationRule> b)
    {
        b.ToTable("SlaEscalationRules");

        b.Property(x => x.NameAr).IsRequired().HasMaxLength(200);
        b.Property(x => x.NameEn).IsRequired().HasMaxLength(200);
        b.Property(x => x.NotifyRole).HasMaxLength(100);

        b.HasOne(x => x.Policy).WithMany(x => x.EscalationRules)
            .HasForeignKey(x => x.SlaPolicyId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.SlaPolicyId, x.IsActive });
    }
}

public sealed class SlaEscalationEventConfiguration : IEntityTypeConfiguration<SlaEscalationEvent>
{
    public void Configure(EntityTypeBuilder<SlaEscalationEvent> b)
    {
        b.ToTable("SlaEscalationEvents");

        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Rule).WithMany()
            .HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);

        // The evaluator asks "has this rule already fired for this ticket?" on every sweep,
        // and the unique constraint is what makes a rule fire exactly once even if two
        // sweeps overlap.
        b.HasIndex(x => new { x.TicketId, x.RuleId }).IsUnique();

        // The event is append-only and so not soft-deletable, but its rule is. Without a
        // matching filter EF warns that the required end can be filtered away; mirroring the
        // rule's filter here keeps the two consistent. A retired rule's history disappearing
        // with it is the intended reading — the rule no longer fires, so its dedupe records
        // have nothing left to guard. This follows TicketHistory, which filters on its
        // parent ticket for the same reason.
        b.HasQueryFilter(x => !x.Rule!.IsDeleted);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");

        b.Property(x => x.ParametersJson).HasMaxLength(2000);

        b.Ignore(x => x.IsRead);

        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);

        // The bell badge counts unread rows for one user on every page load, so that is the
        // query the index is shaped for.
        b.HasIndex(x => new { x.UserId, x.ReadAt, x.CreatedAt });
    }
}
