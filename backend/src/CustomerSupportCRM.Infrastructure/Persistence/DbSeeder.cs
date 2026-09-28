using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CustomerSupportCRM.Infrastructure.Persistence;

/// <summary>Seeds the reference data the system cannot start without: roles, an initial
/// administrator, departments, branches and a starter category tree. Idempotent — safe to
/// run on every startup.</summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AppDbContext>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        await SeedRolesAsync(roleManager, logger);
        var departments = await SeedDepartmentsAsync(db, cancellationToken);
        await SeedBranchesAsync(db, cancellationToken);
        await SeedCategoriesAsync(db, departments, cancellationToken);
        await SeedSystemConfigAsync(db, cancellationToken);
        await SeedSlaAsync(db, cancellationToken);
        await SeedAdminAsync(userManager, configuration, departments, logger);
    }

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager, ILogger logger)
    {
        var descriptions = new Dictionary<string, (string Ar, string En)>
        {
            [Roles.Admin] = ("مدير النظام", "System administrator"),
            [Roles.Manager] = ("مدير الدعم", "Support manager"),
            [Roles.Agent] = ("موظف دعم", "Support agent"),
            [Roles.Customer] = ("عميل", "Portal customer")
        };

        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role)) continue;

            var (ar, en) = descriptions[role];
            var result = await roleManager.CreateAsync(new ApplicationRole(role)
            {
                DescriptionAr = ar,
                DescriptionEn = en
            });

            if (result.Succeeded)
                logger.LogInformation("Seeded role {Role}", role);
            else
                logger.LogError("Failed to seed role {Role}: {Errors}", role,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task<List<Department>> SeedDepartmentsAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Departments.AnyAsync(ct))
            return await db.Departments.ToListAsync(ct);

        var departments = new List<Department>
        {
            new() { Code = "SUP", NameAr = "الدعم الفني", NameEn = "Technical Support" },
            new() { Code = "BIL", NameAr = "الفواتير والحسابات", NameEn = "Billing" },
            new() { Code = "SAL", NameAr = "المبيعات", NameEn = "Sales" },
            new() { Code = "OPS", NameAr = "العمليات", NameEn = "Operations" }
        };

        db.Departments.AddRange(departments);
        await db.SaveChangesAsync(ct);
        return departments;
    }

    private static async Task SeedBranchesAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.Branches.AnyAsync(ct)) return;

        db.Branches.AddRange(
            new Branch { Code = "RUH", NameAr = "الرياض", NameEn = "Riyadh", City = "Riyadh" },
            new Branch { Code = "JED", NameAr = "جدة", NameEn = "Jeddah", City = "Jeddah" },
            new Branch { Code = "DMM", NameAr = "الدمام", NameEn = "Dammam", City = "Dammam" });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCategoriesAsync(AppDbContext db, List<Department> departments, CancellationToken ct)
    {
        if (await db.TicketCategories.AnyAsync(ct)) return;

        var support = departments.FirstOrDefault(d => d.Code == "SUP");
        var billing = departments.FirstOrDefault(d => d.Code == "BIL");

        var technical = new TicketCategory
        {
            NameAr = "مشكلة تقنية", NameEn = "Technical Issue",
            DepartmentId = support?.Id, SortOrder = 1
        };

        var billingCategory = new TicketCategory
        {
            NameAr = "استفسار عن فاتورة", NameEn = "Billing Enquiry",
            DepartmentId = billing?.Id, SortOrder = 2
        };

        var general = new TicketCategory { NameAr = "استفسار عام", NameEn = "General Enquiry", SortOrder = 3 };
        var complaint = new TicketCategory { NameAr = "شكوى", NameEn = "Complaint", SortOrder = 4 };

        db.TicketCategories.AddRange(technical, billingCategory, general, complaint);
        await db.SaveChangesAsync(ct);

        db.TicketCategories.AddRange(
            new TicketCategory { NameAr = "لا يمكن تسجيل الدخول", NameEn = "Cannot Sign In", ParentId = technical.Id, DepartmentId = support?.Id, SortOrder = 1 },
            new TicketCategory { NameAr = "بطء في الأداء", NameEn = "Performance Problem", ParentId = technical.Id, DepartmentId = support?.Id, SortOrder = 2 },
            new TicketCategory { NameAr = "خطأ في النظام", NameEn = "System Error", ParentId = technical.Id, DepartmentId = support?.Id, SortOrder = 3 },
            new TicketCategory { NameAr = "فاتورة غير صحيحة", NameEn = "Incorrect Invoice", ParentId = billingCategory.Id, DepartmentId = billing?.Id, SortOrder = 1 },
            new TicketCategory { NameAr = "طلب استرداد", NameEn = "Refund Request", ParentId = billingCategory.Id, DepartmentId = billing?.Id, SortOrder = 2 });

        await db.SaveChangesAsync(ct);
    }


    /// <summary>Seeds runtime configuration so a fresh database behaves like the shipped
    /// defaults: a Sunday-to-Thursday working week (the Saudi norm), and branding matching
    /// the CSS custom properties already declared in the frontend.</summary>
    private static async Task SeedSystemConfigAsync(AppDbContext db, CancellationToken ct)
    {
        if (!await db.BusinessHours.AnyAsync(ct))
        {
            var workingDays = new[]
            {
                DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday,
                DayOfWeek.Wednesday, DayOfWeek.Thursday
            };

            db.BusinessHours.AddRange(Enum.GetValues<DayOfWeek>().Select(day => new BusinessHours
            {
                Day = day,
                IsWorkingDay = workingDays.Contains(day),
                OpenAt = workingDays.Contains(day) ? new TimeSpan(8, 0, 0) : null,
                CloseAt = workingDays.Contains(day) ? new TimeSpan(17, 0, 0) : null
            }));
        }

        if (!await db.BrandingSettings.AnyAsync(ct))
        {
            db.BrandingSettings.Add(new BrandingSetting
            {
                CompanyNameAr = "نظام دعم العملاء",
                CompanyNameEn = "Customer Support CRM",
                PrimaryColor = "#0f766e",
                DefaultLocale = "ar"
            });
        }

        if (!await db.ChannelToggles.AnyAsync(ct))
        {
            // Every channel starts disabled: none has credentials yet, and a half-configured
            // channel silently dropping customer messages is worse than an off one.
            db.ChannelToggles.AddRange(Enum.GetValues<CommunicationChannel>()
                .Select(channel => new ChannelToggle { Channel = channel, IsEnabled = false }));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>A default, desk-wide SLA policy so tickets carry targets from the first day.
    ///
    /// It names no department, branch or category, which makes it the least specific policy
    /// and therefore the fallback: any policy an administrator adds later automatically wins
    /// over it without anything having to be deleted.</summary>
    private static async Task SeedSlaAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.SlaPolicies.AnyAsync(ct)) return;

        var policy = new SlaPolicy
        {
            NameAr = "مستوى الخدمة الافتراضي",
            NameEn = "Default service level",
            IsActive = true,
            Rank = 0,
            CountsBusinessHoursOnly = true,
            PausedStatuses = nameof(TicketStatus.Pending),
            AssignmentStrategy = AutoAssignmentStrategy.None
        };

        // Working minutes, against the seeded Sunday-Thursday 08:00-17:00 week. Urgent is
        // deliberately tight enough to breach within a single day so the escalation rules
        // below are exercised rather than theoretical.
        var targets = new (TicketPriority Priority, int FirstResponse, int Resolution)[]
        {
            (TicketPriority.Low, 480, 4320),
            (TicketPriority.Normal, 240, 2160),
            (TicketPriority.High, 120, 960),
            (TicketPriority.Urgent, 30, 240)
        };

        foreach (var (priority, firstResponse, resolution) in targets)
        {
            policy.Targets.Add(new SlaTarget
            {
                SlaPolicyId = policy.Id,
                Priority = priority,
                FirstResponseMinutes = firstResponse,
                ResolutionMinutes = resolution
            });
        }

        // Two rules per clock: a warning while there is still time to act, and a breach
        // notice that also raises the escalation level.
        policy.EscalationRules.Add(new SlaEscalationRule
        {
            SlaPolicyId = policy.Id,
            NameAr = "تحذير قرب تجاوز الاستجابة الأولى",
            NameEn = "First response approaching breach",
            Target = SlaTargetKind.FirstResponse,
            ThresholdPercent = 80,
            RaiseLevelBy = 0,
            NotifyRole = Roles.Manager
        });

        policy.EscalationRules.Add(new SlaEscalationRule
        {
            SlaPolicyId = policy.Id,
            NameAr = "تجاوز الاستجابة الأولى",
            NameEn = "First response breached",
            Target = SlaTargetKind.FirstResponse,
            ThresholdPercent = 100,
            RaiseLevelBy = 1,
            NotifyRole = Roles.Manager
        });

        policy.EscalationRules.Add(new SlaEscalationRule
        {
            SlaPolicyId = policy.Id,
            NameAr = "تجاوز مدة الحل",
            NameEn = "Resolution breached",
            Target = SlaTargetKind.Resolution,
            ThresholdPercent = 100,
            RaiseLevelBy = 1,
            NotifyRole = Roles.Manager
        });

        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdminAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        List<Department> departments,
        ILogger logger)
    {
        var email = configuration["Seed:AdminEmail"] ?? "admin@azm.com.sa";

        // Check role membership separately from account existence. A seed run that fails
        // between CreateAsync and AddToRoleAsync would otherwise leave a permanently
        // role-less administrator that no later run repairs.
        if (await userManager.FindByEmailAsync(email) is { } existing)
        {
            await EnsureAdminRoleAsync(userManager, existing, logger);
            return;
        }

        // No fallback on purpose. A default password here would silently create a
        // known-credential administrator on any clone that forgot to configure one, and
        // appsettings.Development.json is git-ignored precisely so that value is never
        // shared. Fail closed instead: no admin is created, and the log says why.
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogError(
                "Seed:AdminPassword is not configured, so no administrator was created. " +
                "Copy appsettings.Development.example.json to appsettings.Development.json " +
                "and set Seed:AdminPassword, then restart.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullNameAr = "مدير النظام",
            FullNameEn = "System Administrator",
            PreferredLanguage = "ar",
            DepartmentId = departments.FirstOrDefault(d => d.Code == "SUP")?.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await userManager.CreateAsync(admin, password);

        if (!result.Succeeded)
        {
            logger.LogError("Failed to seed administrator {Email}: {Errors}", email,
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await EnsureAdminRoleAsync(userManager, admin, logger);
        logger.LogWarning(
            "Seeded administrator {Email} with the default development password. Change it before deploying.", email);
    }

    private static async Task EnsureAdminRoleAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser user, ILogger logger)
    {
        if (await userManager.IsInRoleAsync(user, Roles.Admin)) return;

        var result = await userManager.AddToRoleAsync(user, Roles.Admin);

        if (result.Succeeded)
            logger.LogInformation("Granted the {Role} role to {Email}.", Roles.Admin, user.Email);
        else
            logger.LogError("Failed to grant the {Role} role to {Email}: {Errors}", Roles.Admin, user.Email,
                string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
