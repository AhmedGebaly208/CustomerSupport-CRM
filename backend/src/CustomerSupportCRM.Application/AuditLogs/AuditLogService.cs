using CustomerSupportCRM.Application.AuditLogs.Dtos;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.AuditLogs;

/// <summary>Read-only access to the audit trail. There is deliberately no create, update or
/// delete method: rows are written solely by the persistence interceptor, and an
/// application-level write path would undermine the trail's value as evidence.</summary>
public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct = default);
    Task<AuditLogDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<AuditLogFacetsDto> GetFacetsAsync(CancellationToken ct = default);
}

public sealed class AuditLogService(IAppDbContext db, IIdentityService identity) : IAuditLogService
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.EntityName))
        {
            var entityName = query.EntityName.Trim();
            q = q.Where(a => a.EntityName == entityName);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            var entityId = query.EntityId.Trim();
            q = q.Where(a => a.EntityId == entityId);
        }

        if (query.Action is { } action) q = q.Where(a => a.Action == action);
        if (query.UserId is { } userId) q = q.Where(a => a.UserId == userId);
        if (query.DateFrom is { } from) q = q.Where(a => a.OccurredAt >= from);
        if (query.DateTo is { } to) q = q.Where(a => a.OccurredAt <= to);

        // Free-text search spans the entity name and the change payload, so an admin can
        // find "who touched FullNameEn" without knowing the entity up front.
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(a =>
                a.EntityName.Contains(term) ||
                a.EntityId.Contains(term) ||
                (a.UserName != null && a.UserName.Contains(term)) ||
                (a.Changes != null && a.Changes.Contains(term)));
        }

        var total = await q.CountAsync(ct);

        // Newest first: an audit investigation almost always starts from "what just happened".
        q = query.SortBy?.ToLowerInvariant() switch
        {
            "entityname" => query.SortDescending ? q.OrderByDescending(a => a.EntityName) : q.OrderBy(a => a.EntityName),
            "action" => query.SortDescending ? q.OrderByDescending(a => a.Action) : q.OrderBy(a => a.Action),
            "occurredat" => query.SortDescending ? q.OrderByDescending(a => a.OccurredAt) : q.OrderBy(a => a.OccurredAt),
            _ => q.OrderByDescending(a => a.OccurredAt)
        };

        var rows = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(a => new Row(
                a.Id, a.EntityName, a.EntityId, a.Action, a.Changes,
                a.UserId, a.UserName, a.IpAddress, a.OccurredAt))
            .ToListAsync(ct);

        var items = await ToDtosAsync(rows, ct);
        return PagedResult<AuditLogDto>.Create(items, total, query.Page, query.PageSize);
    }

    public async Task<AuditLogDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new Row(
                a.Id, a.EntityName, a.EntityId, a.Action, a.Changes,
                a.UserId, a.UserName, a.IpAddress, a.OccurredAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(AuditLog), id);

        return (await ToDtosAsync([row], ct))[0];
    }

    public async Task<AuditLogFacetsDto> GetFacetsAsync(CancellationToken ct = default)
    {
        var entityNames = await db.AuditLogs.AsNoTracking()
            .Select(a => a.EntityName)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(ct);

        return new AuditLogFacetsDto(entityNames);
    }

    private sealed record Row(
        Guid Id, string EntityName, string EntityId, Domain.Enums.AuditAction Action, string? Changes,
        Guid? UserId, string? UserName, string? IpAddress, DateTimeOffset OccurredAt);

    /// <summary>Resolves actor names in bulk. The trail stores the display name at write time,
    /// but a later rename should show the current name, so the live lookup wins when it
    /// resolves and the stored value is the fallback for deleted accounts.</summary>
    private async Task<IReadOnlyList<AuditLogDto>> ToDtosAsync(IReadOnlyList<Row> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct();
        var names = await identity.GetUserDisplayNamesAsync(userIds, ct);

        return rows.Select(r =>
        {
            var current = r.UserId is { } id && names.TryGetValue(id, out var name) ? name : null;

            return new AuditLogDto(
                r.Id, r.EntityName, r.EntityId, r.Action, r.Changes,
                r.UserId,
                current ?? r.UserName,
                r.IpAddress,
                r.OccurredAt);
        }).ToList();
    }
}
