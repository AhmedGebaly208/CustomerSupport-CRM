using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Channels;
using CustomerSupportCRM.Application.Auth.Dtos;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Sla;
using CustomerSupportCRM.Domain.Tickets;

namespace CustomerSupportCRM.Application.Tests.TestSupport;

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public string? UserName { get; set; } = "Test Agent";
    public string? Email { get; set; } = "agent@azm.com.sa";
    public IReadOnlyList<string> Roles { get; set; } = ["Agent"];
    public bool IsAuthenticated => UserId is not null;
    public bool IsInRole(string role) => Roles.Contains(role);

    /// <summary>Arbitrary claims a test can set, e.g. department_id / branch_id for scoping.</summary>
    public Dictionary<string, string?> Claims { get; } = new();

    /// <summary>Permissions the fake caller holds. Defaults to every permission the caller's
    /// roles grant, so existing tests keep the access they had before the permission model.</summary>
    public HashSet<string> Permissions { get; set; } = [.. RolePermissions.ForRoles(["Agent"])];

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public string? FindClaim(string claimType) => Claims.GetValueOrDefault(claimType);
}

/// <summary>Scope stand-in. Defaults to global so the existing service tests, which are
/// about business rules rather than scoping, keep seeing every row.</summary>
public sealed class FakeScopeProvider : IScopeProvider
{
    public bool IsGlobal { get; set; } = true;
    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }

    public IQueryable<T> Apply<T>(IQueryable<T> source) where T : class, IScopedEntity
    {
        if (IsGlobal) return source;
        if (DepartmentId is null) return source.Where(_ => false);

        var departmentId = DepartmentId;
        return source.Where(e => e.DepartmentId == null || e.DepartmentId == departmentId);
    }

    public bool CanAccess(IScopedEntity entity)
    {
        if (IsGlobal) return true;
        if (DepartmentId is null) return false;
        return entity.DepartmentId is null || entity.DepartmentId == DepartmentId;
    }

    public void EnsureCanAccess(IScopedEntity entity)
    {
        if (!CanAccess(entity))
            throw new ForbiddenException("This record belongs to another department.");
    }

    /// <summary>Narrows the caller to one department, as a non-supervisory agent would be.</summary>
    public void ScopeTo(Guid? departmentId)
    {
        IsGlobal = false;
        DepartmentId = departmentId;
    }
}

/// <summary>Fixed clock so SLA fields and history timestamps are assertable.</summary>
public sealed class FakeClock(DateTimeOffset? start = null) : IClock
{
    public DateTimeOffset UtcNow { get; set; } =
        start ?? new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>In-memory stand-in for the SQL sequence generator.</summary>
public sealed class FakeReferenceNumberGenerator : IReferenceNumberGenerator
{
    private long _ticket;
    private long _customer;

    public Task<string> NextTicketNumberAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ReferenceNumber.Ticket(Interlocked.Increment(ref _ticket)));

    public Task<string> NextCustomerCodeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ReferenceNumber.Customer(Interlocked.Increment(ref _customer)));
}

/// <summary>Identity stand-in: Application code only needs agent lookup and display names,
/// so the tests do not have to spin up ASP.NET Identity.</summary>
public sealed class FakeIdentityService : IIdentityService
{
    public List<AgentDto> Agents { get; } = [];

    public Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Authentication is exercised through the API, not these tests.");

    public Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Authentication is exercised through the API, not these tests.");

    public Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CurrentUserDto(userId, "test@azm.com.sa", "مستخدم", "User", "ar", null, null,
            ["Agent"], [.. RolePermissions.For("Agent")]));

    public Task<IReadOnlyList<AgentDto>> GetAgentsAsync(Guid? departmentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentDto>>(
            departmentId is null ? Agents : Agents.Where(a => a.DepartmentId == departmentId).ToList());

    /// <summary>Role membership is not modelled in the fake agent list, so this returns
    /// nothing. Tests that care about role-targeted notifications set the recipients
    /// directly instead.</summary>
    public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(
        string roleName, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<IReadOnlyDictionary<Guid, string>> GetUserDisplayNamesAsync(
        IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        var names = Agents
            .Where(a => ids.Contains(a.Id))
            .ToDictionary(a => a.Id, a => a.FullNameEn);

        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(names);
    }

    public AgentDto AddAgent(string name, Guid? departmentId = null)
    {
        var agent = new AgentDto(Guid.NewGuid(), name, name, $"{name.ToLowerInvariant()}@azm.com.sa", departmentId, 0);
        Agents.Add(agent);
        return agent;
    }

    // ---- User administration ----
    // These live on IIdentityService because Application must not reference ASP.NET Identity.
    // They are exercised against the real UserManager in IdentityServiceAdminTests, which uses
    // a SQLite-backed context; the Application-layer tests that use this fake never call them.

    private const string NotExercisedHere =
        "User administration is exercised in IdentityServiceAdminTests against the real UserManager.";

    public Task<PagedResult<UserAdminDto>> ListUsersAsync(UserListQuery query, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task<UserAdminDto> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task<UserAdminDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task<UserAdminDto> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task DeactivateUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task ReactivateUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task SetUserRolesAsync(Guid userId, IReadOnlyList<string> roleNames, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);

    public Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotExercisedHere);
}

/// <summary>In-memory file storage. Keeps bytes in a dictionary so attachment behaviour can
/// be exercised without touching the disk.</summary>
public sealed class FakeFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = [];

    /// <summary>Extensions the fake refuses, mirroring the real allow-list closely enough
    /// to exercise the rejection path.</summary>
    public HashSet<string> AllowedExtensions { get; } =
        [".pdf", ".png", ".jpg", ".txt", ".csv", ".xlsx", ".docx"];

    public async Task<string> SaveAsync(
        Stream content, string fileName, string subFolder, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (!AllowedExtensions.Contains(extension))
            throw new InvalidOperationException($"File type '{extension}' is not allowed.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var path = $"{subFolder}/{Guid.NewGuid():N}{extension}";
        _files[path] = buffer.ToArray();
        return path;
    }

    public Task<Stream?> OpenAsync(string relativePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(_files.TryGetValue(relativePath, out var bytes)
            ? new MemoryStream(bytes)
            : null);

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        _files.Remove(relativePath);
        return Task.CompletedTask;
    }
}

/// <summary>Runs the unit of work inline. The in-memory provider has no real transactions,
/// and these tests assert on outcomes rather than on isolation semantics.</summary>
public sealed class FakeTransactionRunner : ITransactionRunner
{
    public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) => work();

    public async Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) => await work();
}

/// <summary>A calendar that is always open, so SLA arithmetic in tests is plain elapsed time
/// and a test written on a Thursday does not behave differently from one written on a Monday.
/// Tests that exercise working hours build their own BusinessCalendar instead.</summary>
public sealed class FakeBusinessCalendarProvider : IBusinessCalendarProvider
{
    public BusinessCalendar Calendar { get; set; } = BusinessCalendar.TwentyFourSeven(TimeZoneInfo.Utc);

    public Task<BusinessCalendar> GetAsync(CancellationToken ct = default) => Task.FromResult(Calendar);
}

/// <summary>Records what would have been sent without touching a provider. Ticket tests care
/// that a reply is queued, not how it travels.</summary>
public sealed class FakeOutboundDispatcher : IOutboundDispatcher
{
    public List<(Guid TicketId, Guid CommentId)> Queued { get; } = [];

    public Task QueueAsync(Guid ticketId, Guid commentId, CancellationToken ct = default)
    {
        Queued.Add((ticketId, commentId));
        return Task.CompletedTask;
    }

    public Task<int> DispatchPendingAsync(CancellationToken ct = default) => Task.FromResult(0);
}
