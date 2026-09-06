using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Infrastructure.Identity;
using CustomerSupportCRM.Infrastructure.Persistence;
using CustomerSupportCRM.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportCRM.Application.Tests.TestSupport;

/// <summary>Runs <see cref="IdentityService"/> against a real <see cref="UserManager{T}"/>
/// and <see cref="RoleManager{T}"/>, because the lockout rails this fixture exists to test
/// (last-admin protection, refresh-token revocation) depend on real Identity behaviour and
/// would be meaningless against a hand-written fake.
///
/// The store is EF InMemory. Transactions are no-ops there, which is fine: the guards are
/// asserted on their outcome, not on isolation semantics.</summary>
public sealed class IdentityHarness : IDisposable
{
    private readonly ServiceProvider _provider;

    public IdentityHarness()
    {
        CurrentUser = new FakeCurrentUser();
        Clock = new FakeClock();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton(new AuditingInterceptor(CurrentUser, Clock));

        var databaseName = $"identity-tests-{Guid.NewGuid()}";

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseInMemoryDatabase(databaseName);
            options.AddInterceptors(sp.GetRequiredService<AuditingInterceptor>());
            options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddSingleton(Options.Create(new JwtOptions
        {
            // 32+ characters, matching the startup guard in Program.cs.
            SigningKey = "identity-harness-signing-key-not-a-real-secret",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        }));

        _provider = services.BuildServiceProvider();

        Db = _provider.GetRequiredService<AppDbContext>();
        UserManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager = _provider.GetRequiredService<RoleManager<ApplicationRole>>();

        Identity = new IdentityService(
            UserManager,
            Db,
            CurrentUser,
            _provider.GetRequiredService<IOptions<JwtOptions>>(),
            Clock);

        SeedRoles();
    }

    public AppDbContext Db { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public RoleManager<ApplicationRole> RoleManager { get; }
    public FakeCurrentUser CurrentUser { get; }
    public FakeClock Clock { get; }
    public IIdentityService Identity { get; }

    public const string ValidPassword = "Str0ng!Passw0rd";

    private void SeedRoles()
    {
        foreach (var role in Roles.All)
        {
            RoleManager.CreateAsync(new ApplicationRole(role)).GetAwaiter().GetResult();
        }
    }

    public Department SeedDepartment(string code = "SUP")
    {
        var department = new Department { Code = code, NameAr = "الدعم", NameEn = "Support" };
        Db.Departments.Add(department);
        Db.SaveChanges();
        return department;
    }

    /// <summary>Creates a user directly through Identity, bypassing the admin service, so a
    /// test can arrange state without depending on the code under test.</summary>
    public async Task<ApplicationUser> SeedUserAsync(
        string email, string role, bool isActive = true, Guid? departmentId = null)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullNameAr = "مستخدم",
            FullNameEn = email.Split('@')[0],
            PreferredLanguage = "ar",
            DepartmentId = departmentId,
            IsActive = isActive,
            CreatedAt = Clock.UtcNow
        };

        var created = await UserManager.CreateAsync(user, ValidPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        var added = await UserManager.AddToRoleAsync(user, role);
        Assert.True(added.Succeeded, string.Join("; ", added.Errors.Select(e => e.Description)));

        return user;
    }

    /// <summary>Signs the given user in as the acting caller for subsequent admin calls.</summary>
    public void ActAs(ApplicationUser user, params string[] roles)
    {
        CurrentUser.UserId = user.Id;
        CurrentUser.Email = user.Email;
        CurrentUser.UserName = user.FullNameEn;
        CurrentUser.Roles = roles.Length > 0 ? roles : [Roles.Admin];
    }

    public void Dispose()
    {
        Db.Dispose();
        _provider.Dispose();
    }
}
