using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class BusinessHoursConfiguration : IEntityTypeConfiguration<BusinessHours>
{
    public void Configure(EntityTypeBuilder<BusinessHours> b)
    {
        b.ToTable("BusinessHours");

        // One row per weekday; the filter keeps a soft-deleted row from blocking a reinsert.
        b.HasIndex(x => x.Day).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.ToTable("Holidays");

        b.Property(x => x.NameAr).IsRequired().HasMaxLength(150);
        b.Property(x => x.NameEn).IsRequired().HasMaxLength(150);

        b.HasIndex(x => x.Date).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class BrandingSettingConfiguration : IEntityTypeConfiguration<BrandingSetting>
{
    public void Configure(EntityTypeBuilder<BrandingSetting> b)
    {
        b.ToTable("BrandingSettings");

        b.Property(x => x.CompanyNameAr).IsRequired().HasMaxLength(120);
        b.Property(x => x.CompanyNameEn).IsRequired().HasMaxLength(120);
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.Property(x => x.PrimaryColor).HasMaxLength(9);
        b.Property(x => x.SecondaryColor).HasMaxLength(9);
        b.Property(x => x.DefaultLocale).IsRequired().HasMaxLength(8);
    }
}

public sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlag>
{
    public void Configure(EntityTypeBuilder<FeatureFlag> b)
    {
        b.ToTable("FeatureFlags");

        b.Property(x => x.Key).IsRequired().HasMaxLength(100);
        b.Property(x => x.DescriptionAr).HasMaxLength(300);
        b.Property(x => x.DescriptionEn).HasMaxLength(300);

        b.HasIndex(x => x.Key).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class ChannelToggleConfiguration : IEntityTypeConfiguration<ChannelToggle>
{
    public void Configure(EntityTypeBuilder<ChannelToggle> b)
    {
        b.ToTable("ChannelToggles");

        b.Property(x => x.ApiKey).HasMaxLength(500);
        b.Property(x => x.ApiSecret).HasMaxLength(500);
        b.Property(x => x.Endpoint).HasMaxLength(500);

        b.HasIndex(x => x.Channel).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
