using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.SystemConfig;
using CustomerSupportCRM.Application.SystemConfig.Dtos;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Runtime system configuration (PDF area 10) and branding (area 12).
///
/// Reads require systemconfig.view; writes require systemconfig.manage, which only Admin
/// holds. The one exception is <c>GET /api/branding</c>, which is anonymous — see the
/// comment on that action.</summary>
[ApiController]
[Route("api/system-config")]
[Authorize(Policy = Permissions.SystemConfig.View)]
public sealed class SystemConfigController(ISystemConfigService config) : ControllerBase
{
    /// <summary>Branding for the SPA shell.
    ///
    /// Deliberately anonymous: the login screen renders before anyone has a token, and it
    /// needs the company name, logo and primary colour. The payload carries no customer or
    /// operational data, so this is the single, intentional opt-out from the default
    /// authenticated policy — it must not be used as a precedent for other endpoints.</summary>
    [HttpGet("/api/branding")]
    [AllowAnonymous]
    [ProducesResponseType<BrandingDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrandingDto>> GetBranding(CancellationToken ct) =>
        Ok(await config.GetBrandingAsync(ct));

    [HttpPut("/api/branding")]
    [Authorize(Policy = Permissions.SystemConfig.Manage)]
    [ProducesResponseType<BrandingDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<BrandingDto>> UpdateBranding(UpdateBrandingRequest request, CancellationToken ct) =>
        Ok(await config.UpdateBrandingAsync(request, ct));

    // ---- Business hours ----

    [HttpGet("business-hours")]
    [ProducesResponseType<IReadOnlyList<BusinessHoursDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BusinessHoursDto>>> GetBusinessHours(CancellationToken ct) =>
        Ok(await config.GetBusinessHoursAsync(ct));

    [HttpPut("business-hours")]
    [Authorize(Policy = Permissions.SystemConfig.Manage)]
    [ProducesResponseType<IReadOnlyList<BusinessHoursDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<BusinessHoursDto>>> SaveBusinessHours(
        SaveBusinessHoursRequest request, CancellationToken ct) =>
        Ok(await config.SaveBusinessHoursAsync(request, ct));

    // ---- Holidays ----

    [HttpGet("holidays")]
    [ProducesResponseType<IReadOnlyList<HolidayDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HolidayDto>>> GetHolidays(
        [FromQuery] int? year, CancellationToken ct) =>
        Ok(await config.GetHolidaysAsync(year, ct));

    [HttpPost("holidays")]
    [Authorize(Policy = Permissions.SystemConfig.Manage)]
    [ProducesResponseType<HolidayDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HolidayDto>> AddHoliday(SaveHolidayRequest request, CancellationToken ct)
    {
        var holiday = await config.AddHolidayAsync(request, ct);
        return CreatedAtAction(nameof(GetHolidays), new { year = holiday.Date.Year }, holiday);
    }

    [HttpDelete("holidays/{id:guid}")]
    [Authorize(Policy = Permissions.SystemConfig.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct)
    {
        await config.DeleteHolidayAsync(id, ct);
        return NoContent();
    }

    // ---- Feature flags ----

    [HttpGet("feature-flags")]
    [ProducesResponseType<IReadOnlyList<FeatureFlagDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FeatureFlagDto>>> GetFeatureFlags(CancellationToken ct) =>
        Ok(await config.GetFeatureFlagsAsync(ct));

    [HttpPut("feature-flags")]
    [Authorize(Policy = Permissions.SystemConfig.Manage)]
    [ProducesResponseType<FeatureFlagDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FeatureFlagDto>> SaveFeatureFlag(
        SaveFeatureFlagRequest request, CancellationToken ct) =>
        Ok(await config.SaveFeatureFlagAsync(request, ct));

    // ---- Channels ----

    /// <summary>Channel state. Credentials are never returned — only whether one is set.</summary>
    [HttpGet("channels")]
    [ProducesResponseType<IReadOnlyList<ChannelToggleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChannelToggleDto>>> GetChannels(CancellationToken ct) =>
        Ok(await config.GetChannelsAsync(ct));

    /// <summary>Omit a credential field to leave the stored secret unchanged; send an empty
    /// string to clear it.</summary>
    [HttpPut("channels/{channel}")]
    [Authorize(Policy = Permissions.SystemConfig.Manage)]
    [ProducesResponseType<ChannelToggleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChannelToggleDto>> UpdateChannel(
        CommunicationChannel channel, UpdateChannelToggleRequest request, CancellationToken ct) =>
        Ok(await config.UpdateChannelAsync(channel, request, ct));
}
