using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("ApiKeys");

        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Prefix).IsRequired().HasMaxLength(16);
        b.Property(x => x.KeyHash).IsRequired().HasMaxLength(200);
        b.Property(x => x.Scopes).IsRequired().HasMaxLength(2000);
        b.Property(x => x.RevokedReason).HasMaxLength(500);

        // Every authenticated request looks a key up by its prefix, so that is the index.
        b.HasIndex(x => x.Prefix).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> b)
    {
        b.ToTable("WebhookSubscriptions");

        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Url).IsRequired().HasMaxLength(500);
        b.Property(x => x.Events).IsRequired().HasMaxLength(1000);
        b.Property(x => x.Secret).IsRequired().HasMaxLength(200);

        b.HasIndex(x => x.IsActive);
    }
}

public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> b)
    {
        b.ToTable("WebhookDeliveries");

        b.Property(x => x.EventType).IsRequired().HasMaxLength(100);
        b.Property(x => x.Payload).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(1000);

        b.HasOne(x => x.Subscription).WithMany()
            .HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);

        // The dispatcher's sweep: what is waiting and what is due.
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        b.HasIndex(x => new { x.SubscriptionId, x.CreatedAt });

        // The subscription is soft-deletable and this is not; mirroring its filter keeps the
        // required end from being filtered away.
        b.HasQueryFilter(x => !x.Subscription!.IsDeleted);
    }
}
