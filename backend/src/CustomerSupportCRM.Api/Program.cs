using System.Globalization;
using System.Text;
using CustomerSupportCRM.Api.Auth;
using CustomerSupportCRM.Api.Middleware;
using CustomerSupportCRM.Api.Services;
using CustomerSupportCRM.Application;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Infrastructure;
using CustomerSupportCRM.Infrastructure.Identity;
using CustomerSupportCRM.Infrastructure.Persistence;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// ---- Application services ----

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IScopeProvider, ScopeProvider>();

// Escalation has to happen whether or not anyone is looking at the ticket, so the sweep runs
// on a timer in the host rather than on a request path.
builder.Services.AddHostedService<SlaEvaluatorHostedService>();
builder.Services.AddHostedService<ReminderHostedService>();
builder.Services.AddHostedService<ChannelDispatchHostedService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums cross the wire as numbers, matching the TypeScript enums the frontend
        // generates from /api/lookups/enums.
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddFluentValidationAutoValidation(config =>
{
    config.DisableDataAnnotationsValidation = true;
});

// ---- Bilingual request localization (area 12) ----

var supportedCultures = new[] { new CultureInfo("ar-SA"), new CultureInfo("en-US") };

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    // Arabic is the primary language of the desk; English is the fallback.
    options.DefaultRequestCulture = new RequestCulture("ar-SA");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.ApplyCurrentCultureToResponseHeaders = true;
});

// ---- Authentication ----

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    // Fail at startup rather than issuing tokens signed with a weak or empty key.
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 characters. Set it via user-secrets, " +
        "an environment variable (Jwt__SigningKey), or your key vault.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            // Default is 5 minutes of leeway, which makes short-lived tokens outlive
            // their stated expiry.
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Authenticated by default; endpoints opt out with [AllowAnonymous].
    options.FallbackPolicy = options.DefaultPolicy;
});

// Resolves Permissions.* constants used as policy names into real policies on demand.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

// ---- CORS for the Vue dev server ----

const string FrontendCorsPolicy = "FrontendCors";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// ---- OpenAPI ----

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Customer Support CRM API",
        Version = "v1",
        Description = "Customers, tickets, agent dashboard and lookups."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token from POST /api/auth/login."
    });

    // Swashbuckle 10 hands the document to the callback so the reference can be bound
    // to it; OpenAPI.NET v2 needs a reference object here, not the scheme instance.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath);
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// ---- Pipeline ----

app.UseExceptionHandling();
app.UseSerilogRequestLogging();

app.UseRequestLocalization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Customer Support CRM API v1");
        options.DocumentTitle = "Customer Support CRM API";
    });
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

// ---- Startup migration + seed ----
//
// Controlled by Database:MigrateOnStartup / Database:SeedOnStartup, both true in
// Development and false elsewhere. Auto-migrating a shared or production database on
// startup races between instances and gives a deploy no chance to be reviewed, so those
// environments apply migrations as a deliberate step instead.

if (app.Configuration.GetValue("Database:MigrateOnStartup", app.Environment.IsDevelopment()))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

    if (pending.Count == 0)
    {
        startupLogger.LogInformation("Database is up to date; no migrations to apply.");
    }
    else
    {
        // Named before and after, so a failed run tells you exactly which migration broke.
        startupLogger.LogWarning("Applying {Count} pending migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));

        await db.Database.MigrateAsync();

        startupLogger.LogWarning("Applied {Count} migration(s) successfully.", pending.Count);
    }
}

if (app.Configuration.GetValue("Database:SeedOnStartup", app.Environment.IsDevelopment()))
{
    await DbSeeder.SeedAsync(app.Services);
}

await app.RunAsync();

/// <summary>Exposed so integration tests can drive the host with WebApplicationFactory.</summary>
public partial class Program;
