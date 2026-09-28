using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerSupportCRM.Infrastructure.Persistence.Configurations;

public sealed class ArticleCategoryConfiguration : IEntityTypeConfiguration<ArticleCategory>
{
    public void Configure(EntityTypeBuilder<ArticleCategory> b)
    {
        b.ToTable("ArticleCategories");

        b.Property(x => x.NameAr).IsRequired().HasMaxLength(200);
        b.Property(x => x.NameEn).IsRequired().HasMaxLength(200);

        // Restrict, not cascade: deleting a parent must not silently take a whole branch of
        // published help with it. The service moves or refuses first.
        b.HasOne(x => x.Parent).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ParentId, x.SortOrder });
    }
}

public sealed class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> b)
    {
        b.ToTable("Articles");

        b.Property(x => x.TitleAr).IsRequired().HasMaxLength(300);
        b.Property(x => x.TitleEn).IsRequired().HasMaxLength(300);
        b.Property(x => x.SlugAr).IsRequired().HasMaxLength(160);
        b.Property(x => x.SlugEn).IsRequired().HasMaxLength(160);
        b.Property(x => x.SummaryAr).HasMaxLength(500);
        b.Property(x => x.SummaryEn).HasMaxLength(500);

        b.HasOne(x => x.Category).WithMany(x => x.Articles)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // Filtered on IsDeleted so a soft-deleted article does not hold its slug hostage.
        b.HasIndex(x => x.SlugAr).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => x.SlugEn).IsUnique().HasFilter("[IsDeleted] = 0");

        // The portal and the agent search both filter on status first.
        b.HasIndex(x => new { x.Status, x.IsPublic, x.CategoryId });
        b.HasIndex(x => new { x.Status, x.IsFaq });
    }
}

public sealed class ArticleTagConfiguration : IEntityTypeConfiguration<ArticleTag>
{
    public void Configure(EntityTypeBuilder<ArticleTag> b)
    {
        b.ToTable("ArticleTags");

        b.Property(x => x.Slug).IsRequired().HasMaxLength(80);
        b.Property(x => x.LabelAr).IsRequired().HasMaxLength(100);
        b.Property(x => x.LabelEn).IsRequired().HasMaxLength(100);

        b.HasOne(x => x.Article).WithMany(x => x.Tags)
            .HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ArticleId, x.Slug }).IsUnique();
        b.HasIndex(x => x.Slug);

        b.HasQueryFilter(x => !x.Article!.IsDeleted);
    }
}

public sealed class ArticleVersionConfiguration : IEntityTypeConfiguration<ArticleVersion>
{
    public void Configure(EntityTypeBuilder<ArticleVersion> b)
    {
        b.ToTable("ArticleVersions");

        b.Property(x => x.TitleAr).IsRequired().HasMaxLength(300);
        b.Property(x => x.TitleEn).IsRequired().HasMaxLength(300);
        b.Property(x => x.SummaryAr).HasMaxLength(500);
        b.Property(x => x.SummaryEn).HasMaxLength(500);
        b.Property(x => x.ChangeNote).HasMaxLength(500);

        b.HasOne(x => x.Article).WithMany(x => x.Versions)
            .HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.ArticleId, x.VersionNumber }).IsUnique();

        // Versions are append-only and so not soft-deletable, but their article is. Mirroring
        // its filter keeps the required end from being filtered away; a deleted article's
        // history goes with it, which is the intended reading.
        b.HasQueryFilter(x => !x.Article!.IsDeleted);
    }
}

public sealed class ArticleVoteConfiguration : IEntityTypeConfiguration<ArticleVote>
{
    public void Configure(EntityTypeBuilder<ArticleVote> b)
    {
        b.ToTable("ArticleVotes");

        b.Property(x => x.VoterKey).IsRequired().HasMaxLength(128);

        b.HasOne(x => x.Article).WithMany()
            .HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);

        // One answer per voter per article, enforced by the database rather than by a check
        // the service could race itself on.
        b.HasIndex(x => new { x.ArticleId, x.VoterKey }).IsUnique();

        b.HasQueryFilter(x => !x.Article!.IsDeleted);
    }
}

public sealed class ArticleTicketLinkConfiguration : IEntityTypeConfiguration<ArticleTicketLink>
{
    public void Configure(EntityTypeBuilder<ArticleTicketLink> b)
    {
        b.ToTable("ArticleTicketLinks");

        b.HasOne(x => x.Article).WithMany()
            .HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);

        // NoAction on the second path: SQL Server allows only one cascade route into a table.
        b.HasOne(x => x.Ticket).WithMany()
            .HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(x => new { x.ArticleId, x.TicketId }).IsUnique();
        b.HasIndex(x => x.TicketId);

        // Both ends are soft-deletable, so both filters are mirrored: a link is only
        // meaningful while the article and the ticket it joins are both live.
        b.HasQueryFilter(x => !x.Article!.IsDeleted && !x.Ticket!.IsDeleted);
    }
}
