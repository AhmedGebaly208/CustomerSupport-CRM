using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Sla;
using CustomerSupportCRM.Application.SystemConfig.Dtos;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.SystemConfig;

public interface ISystemConfigService
{
    Task<BrandingDto> GetBrandingAsync(CancellationToken ct = default);
    Task<BrandingDto> UpdateBrandingAsync(UpdateBrandingRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<BusinessHoursDto>> GetBusinessHoursAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BusinessHoursDto>> SaveBusinessHoursAsync(SaveBusinessHoursRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(int? year, CancellationToken ct = default);
    Task<HolidayDto> AddHolidayAsync(SaveHolidayRequest request, CancellationToken ct = default);
    Task DeleteHolidayAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<FeatureFlagDto>> GetFeatureFlagsAsync(CancellationToken ct = default);
    Task<FeatureFlagDto> SaveFeatureFlagAsync(SaveFeatureFlagRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ChannelToggleDto>> GetChannelsAsync(CancellationToken ct = default);
    Task<ChannelToggleDto> UpdateChannelAsync(CommunicationChannel channel, UpdateChannelToggleRequest request, CancellationToken ct = default);
}

/// <summary>Runtime system configuration (PDF area 10) plus branding (area 12).
///
/// Reads go through <see cref="IConfigCache"/> because branding is fetched on every SPA
/// boot and business hours are read by the SLA evaluator on every tick; every write
/// invalidates the affected entry explicitly rather than relying on a time-based expiry,
/// so a change is visible immediately.</summary>
public sealed class SystemConfigService(IAppDbContext db, IClock clock, IConfigCache cache) : ISystemConfigService
{
    private const string BrandingKey = "config.branding";
    private const string BusinessHoursKey = "config.businesshours";
    private const string FeatureFlagsKey = "config.featureflags";

    // ---- Branding ----

    public Task<BrandingDto> GetBrandingAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync(BrandingKey, async () =>
        {
            var row = await db.BrandingSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            // Falls back to the shipped defaults rather than failing, so a fresh database
            // with no seed row still renders a usable login screen.
            return row is null
                ? new BrandingDto("نظام دعم العملاء", "Customer Support CRM", null, "#0f766e", null, "ar")
                : new BrandingDto(row.CompanyNameAr, row.CompanyNameEn, row.LogoUrl,
                    row.PrimaryColor, row.SecondaryColor, row.DefaultLocale);
        });

    public async Task<BrandingDto> UpdateBrandingAsync(UpdateBrandingRequest request, CancellationToken ct = default)
    {
        var row = await db.BrandingSettings.FirstOrDefaultAsync(ct);

        if (row is null)
        {
            row = new BrandingSetting();
            db.BrandingSettings.Add(row);
        }

        row.CompanyNameAr = request.CompanyNameAr.Trim();
        row.CompanyNameEn = request.CompanyNameEn.Trim();
        row.LogoUrl = Normalize(request.LogoUrl);
        row.PrimaryColor = Normalize(request.PrimaryColor);
        row.SecondaryColor = Normalize(request.SecondaryColor);
        row.DefaultLocale = request.DefaultLocale is "en" ? "en" : "ar";

        await db.SaveChangesAsync(ct);
        cache.Remove(BrandingKey);

        return await GetBrandingAsync(ct);
    }

    // ---- Business hours ----

    public Task<IReadOnlyList<BusinessHoursDto>> GetBusinessHoursAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<BusinessHoursDto>>(BusinessHoursKey, async () =>
        {
            var rows = await db.BusinessHours.AsNoTracking().ToListAsync(ct);

            // Always return all seven days in order, so a caller never has to handle a gap.
            return Enum.GetValues<DayOfWeek>()
                .Select(day =>
                {
                    var row = rows.FirstOrDefault(r => r.Day == day);
                    return new BusinessHoursDto(day, row?.IsWorkingDay ?? false, row?.OpenAt, row?.CloseAt);
                })
                .ToList();
        });

    public async Task<IReadOnlyList<BusinessHoursDto>> SaveBusinessHoursAsync(
        SaveBusinessHoursRequest request, CancellationToken ct = default)
    {
        foreach (var day in request.Days)
        {
            if (day.IsWorkingDay && (day.OpenAt is null || day.CloseAt is null))
                throw new BadRequestException($"{day.Day} is marked as a working day but has no opening and closing time.");

            if (day.IsWorkingDay && day.CloseAt <= day.OpenAt)
                throw new BadRequestException($"{day.Day} closes at or before it opens.");
        }

        var existing = await db.BusinessHours.ToListAsync(ct);

        foreach (var day in request.Days)
        {
            var row = existing.FirstOrDefault(r => r.Day == day.Day);

            if (row is null)
            {
                row = new BusinessHours { Day = day.Day };
                db.BusinessHours.Add(row);
            }

            row.IsWorkingDay = day.IsWorkingDay;
            row.OpenAt = day.IsWorkingDay ? day.OpenAt : null;
            row.CloseAt = day.IsWorkingDay ? day.CloseAt : null;
        }

        await db.SaveChangesAsync(ct);
        InvalidateSchedule();

        return await GetBusinessHoursAsync(ct);
    }

    // ---- Holidays ----

    public async Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(int? year, CancellationToken ct = default)
    {
        var q = db.Holidays.AsNoTracking().AsQueryable();

        if (year is { } y)
        {
            var from = new DateOnly(y, 1, 1);
            var to = new DateOnly(y, 12, 31);
            q = q.Where(h => h.Date >= from && h.Date <= to);
        }

        return await q
            .OrderBy(h => h.Date)
            .Select(h => new HolidayDto(h.Id, h.Date, h.NameAr, h.NameEn))
            .ToListAsync(ct);
    }

    public async Task<HolidayDto> AddHolidayAsync(SaveHolidayRequest request, CancellationToken ct = default)
    {
        if (await db.Holidays.AnyAsync(h => h.Date == request.Date, ct))
            throw new ConflictException($"A holiday is already recorded for {request.Date:yyyy-MM-dd}.");

        var holiday = new Holiday
        {
            Date = request.Date,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim()
        };

        db.Holidays.Add(holiday);
        await db.SaveChangesAsync(ct);
        InvalidateSchedule();

        return new HolidayDto(holiday.Id, holiday.Date, holiday.NameAr, holiday.NameEn);
    }

    public async Task DeleteHolidayAsync(Guid id, CancellationToken ct = default)
    {
        var holiday = await db.Holidays.FirstOrDefaultAsync(h => h.Id == id, ct)
            ?? throw new NotFoundException(nameof(Holiday), id);

        holiday.IsDeleted = true;
        holiday.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        InvalidateSchedule();
    }

    /// <summary>Drops both cached views of the working schedule. The SLA calendar is derived
    /// from the same rows as the business-hours list and from the holidays, so a change to
    /// either must clear it — otherwise a newly declared holiday would keep counting as a
    /// working day until the process restarted.</summary>
    private void InvalidateSchedule()
    {
        cache.Remove(BusinessHoursKey);
        cache.Remove(SlaCacheKeys.BusinessCalendar);
    }

    // ---- Feature flags ----

    public Task<IReadOnlyList<FeatureFlagDto>> GetFeatureFlagsAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<FeatureFlagDto>>(FeatureFlagsKey, async () =>
            await db.FeatureFlags.AsNoTracking()
                .OrderBy(f => f.Key)
                .Select(f => new FeatureFlagDto(f.Id, f.Key, f.IsEnabled, f.DescriptionAr, f.DescriptionEn))
                .ToListAsync(ct));

    public async Task<FeatureFlagDto> SaveFeatureFlagAsync(
        SaveFeatureFlagRequest request, CancellationToken ct = default)
    {
        var key = request.Key.Trim();
        var flag = await db.FeatureFlags.FirstOrDefaultAsync(f => f.Key == key, ct);

        if (flag is null)
        {
            flag = new FeatureFlag { Key = key };
            db.FeatureFlags.Add(flag);
        }

        flag.IsEnabled = request.IsEnabled;
        flag.DescriptionAr = Normalize(request.DescriptionAr);
        flag.DescriptionEn = Normalize(request.DescriptionEn);

        await db.SaveChangesAsync(ct);
        cache.Remove(FeatureFlagsKey);

        return new FeatureFlagDto(flag.Id, flag.Key, flag.IsEnabled, flag.DescriptionAr, flag.DescriptionEn);
    }

    // ---- Channels ----

    public async Task<IReadOnlyList<ChannelToggleDto>> GetChannelsAsync(CancellationToken ct = default)
    {
        var rows = await db.ChannelToggles.AsNoTracking().ToListAsync(ct);

        // Every channel appears, configured or not, so the admin screen lists them all.
        return Enum.GetValues<CommunicationChannel>()
            .Select(channel =>
            {
                var row = rows.FirstOrDefault(r => r.Channel == channel);

                return new ChannelToggleDto(
                    row?.Id ?? Guid.Empty,
                    channel,
                    row?.IsEnabled ?? false,
                    row?.Endpoint,
                    // Never the value — only whether one is set.
                    !string.IsNullOrWhiteSpace(row?.ApiKey) || !string.IsNullOrWhiteSpace(row?.ApiSecret));
            })
            .ToList();
    }

    public async Task<ChannelToggleDto> UpdateChannelAsync(
        CommunicationChannel channel, UpdateChannelToggleRequest request, CancellationToken ct = default)
    {
        var row = await db.ChannelToggles.FirstOrDefaultAsync(c => c.Channel == channel, ct);

        if (row is null)
        {
            row = new ChannelToggle { Channel = channel };
            db.ChannelToggles.Add(row);
        }

        row.IsEnabled = request.IsEnabled;
        row.Endpoint = Normalize(request.Endpoint);

        // Null means "leave the stored secret alone"; empty string means "clear it". Treating
        // both as a value would silently wipe a credential every time the form is saved.
        if (request.ApiKey is not null) row.ApiKey = Normalize(request.ApiKey);
        if (request.ApiSecret is not null) row.ApiSecret = Normalize(request.ApiSecret);

        await db.SaveChangesAsync(ct);

        return new ChannelToggleDto(
            row.Id, row.Channel, row.IsEnabled, row.Endpoint,
            !string.IsNullOrWhiteSpace(row.ApiKey) || !string.IsNullOrWhiteSpace(row.ApiSecret));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
