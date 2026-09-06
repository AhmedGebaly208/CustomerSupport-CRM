using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Auth.Dtos;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IIdentityService identity, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(await identity.LoginAsync(request, ct));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct) =>
        Ok(await identity.RefreshAsync(request, ct));

    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");
        return Ok(await identity.GetCurrentUserAsync(userId, ct));
    }

    /// <summary>Changes the caller's own password and invalidates their refresh token, so
    /// other sessions cannot mint a new access token.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");
        await identity.ChangePasswordAsync(userId, request, ct);
        return NoContent();
    }

    /// <summary>Assignable agents, optionally narrowed to one department.</summary>
    [HttpGet("/api/agents")]
    [Authorize(Policy = Permissions.Tickets.Assign)]
    [ProducesResponseType<IReadOnlyList<AgentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AgentDto>>> Agents(
        [FromQuery] Guid? departmentId, CancellationToken ct) =>
        Ok(await identity.GetAgentsAsync(departmentId, ct));
}
