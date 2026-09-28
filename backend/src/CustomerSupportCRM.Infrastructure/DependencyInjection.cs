using CustomerSupportCRM.Application.Ai;
using CustomerSupportCRM.Application.Channels;
using CustomerSupportCRM.Application.Reports;
using CustomerSupportCRM.Domain.Enums;
using CustomerSupportCRM.Infrastructure.Ai;
using CustomerSupportCRM.Infrastructure.Channels;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Infrastructure.Caching;
using CustomerSupportCRM.Infrastructure.Identity;
using CustomerSupportCRM.Infrastructure.Import;
using CustomerSupportCRM.Infrastructure.Persistence;
using CustomerSupportCRM.Infrastructure.Reports;
using CustomerSupportCRM.Infrastructure.Persistence.Interceptors;
using CustomerSupportCRM.Infrastructure.Sla;
using CustomerSupportCRM.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportCRM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. Add it to appsettings.json or the environment.");

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                // Transient SQL failures (Azure SQL throttling, failovers) should retry
                // rather than surface as a 500 to an agent mid-ticket.
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            });

            options.AddInterceptors(sp.GetRequiredService<AuditingInterceptor>());
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IReferenceNumberGenerator, SqlReferenceNumberGenerator>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        // Singleton: the cached configuration is process-wide, not per-request.
        services.AddSingleton<IConfigCache, MemoryConfigCache>();

        services.AddScoped<ITransactionRunner, TransactionRunner>();

        services.AddScoped<IBusinessCalendarProvider, BusinessCalendarProvider>();

        services.Configure<LocalChannelOptions>(configuration.GetSection(LocalChannelOptions.SectionName));
        services.AddScoped<IChannelRegistry, ChannelRegistry>();

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.AddMemoryCache();
        services.AddScoped<IAiCompletionService, LocalAiCompletionService>();
        services.AddScoped<IAiRateLimiter, AiRateLimiter>();
        services.AddScoped<IAiCallLogger, AiCallLogger>();

        services.AddScoped<IReportExporter, ReportExporter>();

        // One adapter instance per channel the local provider covers. Registering them
        // individually rather than one multiplexing adapter keeps IChannelAdapter's
        // "one adapter, one channel" shape, which is what a real provider will fit.
        var localChannels = configuration
            .GetSection($"{LocalChannelOptions.SectionName}:Channels")
            .Get<CommunicationChannel[]>()
            ?? [CommunicationChannel.Email, CommunicationChannel.WhatsApp,
                CommunicationChannel.Sms, CommunicationChannel.LiveChat];

        foreach (var channel in localChannels.Distinct())
        {
            var captured = channel;

            services.AddScoped<IChannelAdapter>(sp => new LocalDropChannelAdapter(
                captured,
                sp.GetRequiredService<IOptions<LocalChannelOptions>>(),
                sp.GetRequiredService<ILogger<LocalDropChannelAdapter>>()));
        }

        // Both parsers are registered; the service picks by file extension and content type.
        services.AddScoped<ICustomerImportParser, CsvCustomerImportParser>();
        services.AddScoped<ICustomerImportParser, XlsxCustomerImportParser>();

        return services;
    }
}
