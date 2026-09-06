using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Auth.Dtos;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.Tests;

public class IdentityServiceAdminTests : IAsyncLifetime, IDisposable
{
    private readonly IdentityHarness _h = new();

    /// <summary>The acting administrator. A second admin exists in most arrangements only
    /// where a test needs the last-admin guard *not* to fire.</summary>
    private Guid _actingAdminId;

    public async Task InitializeAsync()
    {
        var admin = await _h.SeedUserAsync("admin@azm.com.sa", Roles.Admin);
        _actingAdminId = admin.Id;
        _h.ActAs(admin);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _h.Dispose();

    private static CreateUserRequest NewUser(
        string email = "agent@azm.com.sa", string role = Roles.Agent, Guid? departmentId = null) =>
        new(email, IdentityHarness.ValidPassword, "موظف دعم", "Support Agent", "ar", departmentId, null, [role]);

    // ---- Creation ----

    [Fact]
    public async Task Create_persists_the_user_with_its_roles()
    {
        var created = await _h.Identity.CreateUserAsync(NewUser());

        Assert.Equal("agent@azm.com.sa", created.Email);
        Assert.Equal([Roles.Agent], created.Roles);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_email()
    {
        await _h.Identity.CreateUserAsync(NewUser());

        await Assert.ThrowsAsync<ConflictException>(() => _h.Identity.CreateUserAsync(NewUser()));
    }

    [Fact]
    public async Task Create_rejects_an_unknown_role()
    {
        var request = NewUser() with { Roles = ["Supervisor"] };

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Identity.CreateUserAsync(request));
    }

    [Fact]
    public async Task Create_rejects_an_empty_role_list()
    {
        var request = NewUser() with { Roles = [] };

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Identity.CreateUserAsync(request));
    }

    [Fact]
    public async Task Create_rejects_a_department_that_does_not_exist()
    {
        var request = NewUser(departmentId: Guid.NewGuid());

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Identity.CreateUserAsync(request));
    }

    [Fact]
    public async Task Create_rolls_back_the_account_when_the_role_assignment_fails()
    {
        // Roles are validated up front, so drive the failure through a role that passes the
        // known-name check but is absent from the database.
        await _h.RoleManager.DeleteAsync((await _h.RoleManager.FindByNameAsync(Roles.Agent))!);

        await Assert.ThrowsAsync<BadRequestException>(() => _h.Identity.CreateUserAsync(NewUser()));

        Assert.Null(await _h.UserManager.FindByEmailAsync("agent@azm.com.sa"));
    }

    [Fact]
    public async Task Create_requires_an_authenticated_caller()
    {
        _h.CurrentUser.UserId = null;

        await Assert.ThrowsAsync<ForbiddenException>(() => _h.Identity.CreateUserAsync(NewUser()));
    }

    // ---- Lockout protection ----

    [Fact]
    public async Task An_admin_cannot_deactivate_their_own_account()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _h.Identity.DeactivateUserAsync(_actingAdminId));

        Assert.Contains("your own account", ex.Message);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_deactivated()
    {
        // A second admin acts, so the self-deactivation guard is not what fires here.
        var second = await _h.SeedUserAsync("admin2@azm.com.sa", Roles.Admin);
        _h.ActAs(second);
        await _h.Identity.DeactivateUserAsync(_actingAdminId);

        // `second` is now the only active admin; deactivating them needs a third actor.
        var manager = await _h.SeedUserAsync("manager@azm.com.sa", Roles.Manager);
        _h.ActAs(manager, Roles.Admin);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _h.Identity.DeactivateUserAsync(second.Id));

        Assert.Contains("last active administrator", ex.Message);
    }

    [Fact]
    public async Task An_admin_can_be_deactivated_while_another_active_admin_remains()
    {
        var second = await _h.SeedUserAsync("admin2@azm.com.sa", Roles.Admin);

        await _h.Identity.DeactivateUserAsync(second.Id);

        var reloaded = await _h.UserManager.FindByIdAsync(second.Id.ToString());
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_lose_the_admin_role()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => _h.Identity.SetUserRolesAsync(_actingAdminId, [Roles.Manager]));

        Assert.Contains("last active administrator", ex.Message);
    }

    [Fact]
    public async Task An_admin_can_lose_the_admin_role_while_another_active_admin_remains()
    {
        await _h.SeedUserAsync("admin2@azm.com.sa", Roles.Admin);

        await _h.Identity.SetUserRolesAsync(_actingAdminId, [Roles.Manager]);

        Assert.Equal([Roles.Manager], await _h.Identity.GetUserRolesAsync(_actingAdminId));
    }

    // ---- Deactivation semantics ----

    [Fact]
    public async Task Deactivation_clears_the_refresh_token()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);

        // Sign in so a refresh token exists to be revoked.
        var login = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));

        await _h.Identity.DeactivateUserAsync(agent.Id);

        var reloaded = await _h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == agent.Id);
        Assert.Null(reloaded.RefreshToken);
        Assert.Null(reloaded.RefreshTokenExpiresAt);
    }

    [Fact]
    public async Task Deactivation_is_idempotent()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent, isActive: false);

        await _h.Identity.DeactivateUserAsync(agent.Id);
        await _h.Identity.DeactivateUserAsync(agent.Id);

        var reloaded = await _h.UserManager.FindByIdAsync(agent.Id.ToString());
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_sign_in_and_the_failure_is_indistinguishable()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        await _h.Identity.DeactivateUserAsync(agent.Id);

        var deactivated = await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword)));

        var unknown = await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.LoginAsync(new LoginRequest("nobody@azm.com.sa", IdentityHarness.ValidPassword)));

        var wrongPassword = await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.LoginAsync(new LoginRequest(agent.Email!, "Wr0ng!Passw0rd")));

        // All three must be the same message: any difference is an account-enumeration oracle.
        Assert.Equal(unknown.Message, deactivated.Message);
        Assert.Equal(unknown.Message, wrongPassword.Message);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_refresh_and_the_token_is_spent()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        var login = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword));

        // Deactivate without going through the admin path, so the refresh token survives and
        // the refresh-time IsActive check is what has to catch it.
        var tracked = await _h.Db.Users.SingleAsync(u => u.Id == agent.Id);
        tracked.IsActive = false;
        await _h.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.RefreshAsync(new RefreshRequest(login.RefreshToken)));

        var reloaded = await _h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == agent.Id);
        Assert.Null(reloaded.RefreshToken);
    }

    [Fact]
    public async Task Reactivation_restores_sign_in()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        await _h.Identity.DeactivateUserAsync(agent.Id);

        await _h.Identity.ReactivateUserAsync(agent.Id);

        var login = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword));
        Assert.Equal(agent.Id, login.User.Id);
    }

    // ---- Refresh token rotation ----

    [Fact]
    public async Task Each_refresh_rotates_the_token_so_the_old_one_stops_working()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        var first = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword));

        var second = await _h.Identity.RefreshAsync(new RefreshRequest(first.RefreshToken));

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.RefreshAsync(new RefreshRequest(first.RefreshToken)));
    }

    [Fact]
    public async Task An_expired_refresh_token_is_refused()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        var login = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword));

        _h.Clock.Advance(TimeSpan.FromDays(8));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.RefreshAsync(new RefreshRequest(login.RefreshToken)));
    }

    // ---- Password change ----

    [Fact]
    public async Task Change_password_signs_other_sessions_out()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        var login = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword));
        _h.ActAs(agent, Roles.Agent);

        await _h.Identity.ChangePasswordAsync(
            agent.Id, new ChangePasswordRequest(IdentityHarness.ValidPassword, "New!Passw0rd1"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.RefreshAsync(new RefreshRequest(login.RefreshToken)));

        var reLogin = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, "New!Passw0rd1"));
        Assert.Equal(agent.Id, reLogin.User.Id);
    }

    [Fact]
    public async Task Change_password_refuses_a_wrong_current_password_without_saying_why()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        _h.ActAs(agent, Roles.Agent);

        var wrongCurrent = await Assert.ThrowsAsync<BadRequestException>(
            () => _h.Identity.ChangePasswordAsync(agent.Id, new ChangePasswordRequest("Wr0ng!Passw0rd", "New!Passw0rd1")));

        var weakNew = await Assert.ThrowsAsync<BadRequestException>(
            () => _h.Identity.ChangePasswordAsync(agent.Id, new ChangePasswordRequest(IdentityHarness.ValidPassword, "weak")));

        // Same message either way, so a stolen session cannot be used to confirm the old password.
        Assert.Equal(wrongCurrent.Message, weakNew.Message);
    }

    [Fact]
    public async Task An_agent_cannot_change_someone_elses_password()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        var other = await _h.SeedUserAsync("other@azm.com.sa", Roles.Agent);
        _h.ActAs(agent, Roles.Agent);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _h.Identity.ChangePasswordAsync(other.Id, new ChangePasswordRequest(IdentityHarness.ValidPassword, "New!Passw0rd1")));
    }

    // ---- Listing ----

    [Fact]
    public async Task List_filters_by_search_across_email_and_both_name_languages()
    {
        await _h.Identity.CreateUserAsync(NewUser("sara@azm.com.sa") with
        {
            FullNameAr = "سارة أحمد",
            FullNameEn = "Sara Ahmed"
        });

        foreach (var term in new[] { "sara@", "سارة", "Ahmed" })
        {
            var page = await _h.Identity.ListUsersAsync(new UserListQuery { Search = term });
            Assert.True(page.TotalCount >= 1, $"Search for '{term}' returned nothing.");
        }
    }

    [Fact]
    public async Task List_filters_by_role_and_active_state()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        await _h.Identity.DeactivateUserAsync(agent.Id);

        var agents = await _h.Identity.ListUsersAsync(new UserListQuery { Role = Roles.Agent });
        Assert.Equal(1, agents.TotalCount);

        var activeAgents = await _h.Identity.ListUsersAsync(new UserListQuery { Role = Roles.Agent, IsActive = true });
        Assert.Equal(0, activeAgents.TotalCount);
    }

    [Fact]
    public async Task List_never_exposes_the_password_hash_or_refresh_token()
    {
        await _h.Identity.CreateUserAsync(NewUser());

        var page = await _h.Identity.ListUsersAsync(new UserListQuery());
        var serialised = System.Text.Json.JsonSerializer.Serialize(page);

        Assert.DoesNotContain("passwordHash", serialised, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", serialised, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", serialised, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_resolves_the_department_name_in_both_languages()
    {
        var department = _h.SeedDepartment();
        await _h.Identity.CreateUserAsync(NewUser(departmentId: department.Id));

        var page = await _h.Identity.ListUsersAsync(new UserListQuery { DepartmentId = department.Id });

        var user = Assert.Single(page.Items);
        Assert.Equal("الدعم", user.DepartmentNameAr);
        Assert.Equal("Support", user.DepartmentNameEn);
    }

    [Fact]
    public async Task Get_throws_NotFound_for_an_unknown_id() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Identity.GetUserAsync(Guid.NewGuid()));

    // ---- Update ----

    [Fact]
    public async Task Update_changes_names_and_roles_together()
    {
        var created = await _h.Identity.CreateUserAsync(NewUser());

        var updated = await _h.Identity.UpdateUserAsync(created.Id, new UpdateUserRequest(
            "سارة أحمد", "Sara Ahmed", "en", null, null, [Roles.Manager]));

        Assert.Equal("Sara Ahmed", updated.FullNameEn);
        Assert.Equal("en", updated.PreferredLanguage);
        Assert.Equal([Roles.Manager], updated.Roles);
    }

    [Fact]
    public async Task A_role_change_does_not_revoke_the_refresh_token()
    {
        var agent = await _h.SeedUserAsync("agent@azm.com.sa", Roles.Agent);
        var login = await _h.Identity.LoginAsync(new LoginRequest(agent.Email!, IdentityHarness.ValidPassword));
        _h.ActAs(await _h.UserManager.FindByIdAsync(_actingAdminId.ToString()) ?? agent);

        await _h.Identity.SetUserRolesAsync(agent.Id, [Roles.Manager]);

        // A promotion should not sign someone out mid-ticket. Permission-cache staleness is
        // bounded by the short access-token lifetime instead.
        var refreshed = await _h.Identity.RefreshAsync(new RefreshRequest(login.RefreshToken));
        Assert.NotNull(refreshed.AccessToken);
    }
}
