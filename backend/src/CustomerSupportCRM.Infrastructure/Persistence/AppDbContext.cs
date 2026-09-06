using System.Reflection;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IAppDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<Interaction> Interactions => Set<Interaction>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketCategory> TicketCategories => Set<TicketCategory>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketHistory> TicketHistory => Set<TicketHistory>();

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<BusinessHours> BusinessHours => Set<BusinessHours>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<BrandingSetting> BrandingSettings => Set<BrandingSetting>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<ChannelToggle> ChannelToggles => Set<ChannelToggle>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Shorter Identity table names — AspNetUsers/AspNetRoles read oddly next to
        // the domain tables in reports and DBA tooling.
        builder.Entity<ApplicationUser>().ToTable("Users");
        builder.Entity<ApplicationRole>().ToTable("Roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        // Reference-number sequences. Declared on the model so migrations create them;
        // SqlReferenceNumberGenerator reads NEXT VALUE FOR from these.
        builder.HasSequence<long>(SqlReferenceNumberGenerator.TicketSequence).StartsAt(1).IncrementsBy(1);
        builder.HasSequence<long>(SqlReferenceNumberGenerator.CustomerSequence).StartsAt(1).IncrementsBy(1);

        ApplySoftDeleteFilters(builder);
    }

    /// <summary>Applies the IsDeleted filter to every ISoftDeletable entity by reflection,
    /// so adding an entity cannot accidentally leak deleted rows through a forgotten filter.</summary>
    private static void ApplySoftDeleteFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType)) continue;

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var property = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = System.Linq.Expressions.Expression.Lambda(
                System.Linq.Expressions.Expression.Not(property), parameter);

            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
