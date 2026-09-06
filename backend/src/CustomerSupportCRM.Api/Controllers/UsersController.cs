using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Auth.Dtos;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>User administration (PDF area 10).
///
/// Reads require users.view; every mutation requires users.manage. Only Admin holds
/// users.manage, and Admin plus Manager hold users.view, which preserves the previous
/// Admin-only write surface while letting a Manager see the roster.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Permissions.Users.View)]
public sealed class UsersController(IIdentityService identity) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<UserAdminDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserAdminDto>>> List(
        [FromQuery] UserListQuery query, CancellationToken ct) =>
        Ok(await identity.ListUsersAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserAdminDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserAdminDto>> GetById(Guid id, CancellationToken ct) =>
        Ok(await identity.GetUserAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Permissions.Users.Manage)]
    [ProducesResponseType<UserAdminDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserAdminDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await identity.CreateUserAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Users.Manage)]
    [ProducesResponseType<UserAdminDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserAdminDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        Ok(await identity.UpdateUserAsync(id, request, ct));

    /// <summary>Deactivates the account and clears its refresh token, signing the user out
    /// once their current access token expires. Refused for your own account, and for the
    /// last remaining active administrator.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await identity.DeactivateUserAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reactivate")]
    [Authorize(Policy = Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await identity.ReactivateUserAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/roles")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoles(Guid id, CancellationToken ct) =>
        Ok(await identity.GetUserRolesAsync(id, ct));

    /// <summary>Replaces the user's role set. Refused when it would strip the Admin role
    /// from the last remaining active administrator.</summary>
    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = Permissions.Users.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRoles(Guid id, SetUserRolesRequest request, CancellationToken ct)
    {
        await identity.SetUserRolesAsync(id, request.Roles, ct);
        return NoContent();
    }

    /// <summary>Read-only list of assignable role names, so the admin UI does not hardcode them.</summary>
    [HttpGet("/api/roles")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> AvailableRoles() => Ok(Roles.All);
}
