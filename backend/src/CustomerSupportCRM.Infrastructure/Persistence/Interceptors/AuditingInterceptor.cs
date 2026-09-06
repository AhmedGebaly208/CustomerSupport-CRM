using System.Text.Json;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Domain.Common;
using CustomerSupportCRM.Domain.Entities;
using CustomerSupportCRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CustomerSupportCRM.Infrastructure.Persistence.Interceptors;

/// <summary>Stamps audit columns and writes the AuditLog trail (area 10) on every save.
/// Centralised here so no service can forget to do it, and so the trail cannot be
/// bypassed by writing through a different code path.</summary>
public sealed class AuditingInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
{
    /// <summary>Never captured into the audit trail even when they change.</summary>
    private static readonly HashSet<string> SensitiveProperties =
    [
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
        "RefreshToken", "RefreshTokenExpiresAt", "TwoFactorEnabled",
        // Channel provider credentials (ChannelToggle). Without these the audit trail
        // would store integration secrets in plaintext.
        "ApiKey", "ApiSecret"
    ];

    /// <summary>Writing audit rows for audit rows would recurse forever.</summary>
    private static readonly HashSet<string> ExcludedEntities = [nameof(AuditLog), nameof(TicketHistory)];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        var now = clock.UtcNow;
        var userId = currentUser.UserId;
        var userName = currentUser.UserName;

        var auditRows = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is AuditLog) continue;

            StampAuditColumns(entry, now, userId);

            var entityName = entry.Metadata.ClrType.Name;
            if (ExcludedEntities.Contains(entityName)) continue;

            var action = ResolveAction(entry);
            if (action is null) continue;

            auditRows.Add(new AuditLog
            {
                EntityName = entityName,
                EntityId = ResolveKey(entry),
                Action = action.Value,
                Changes = action == AuditAction.Created ? null : SerializeChanges(entry),
                UserId = userId,
                UserName = userName,
                OccurredAt = now
            });
        }

        if (auditRows.Count > 0)
            context.Set<AuditLog>().AddRange(auditRows);
    }

    private static void StampAuditColumns(EntityEntry entry, DateTimeOffset now, Guid? userId)
    {
        if (entry.Entity is not IAuditableEntity auditable) return;

        switch (entry.State)
        {
            case EntityState.Added:
                auditable.CreatedAt = now;
                auditable.CreatedBy ??= userId;
                break;

            case EntityState.Modified:
                auditable.ModifiedAt = now;
                auditable.ModifiedBy = userId;
                break;
        }
    }

    private static AuditAction? ResolveAction(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => AuditAction.Created,
        // Soft deletes arrive as an update to IsDeleted; record them as deletions so the
        // trail reflects intent rather than the storage mechanism.
        EntityState.Modified when entry.Entity is ISoftDeletable { IsDeleted: true }
            && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified => AuditAction.Deleted,
        EntityState.Modified => AuditAction.Updated,
        EntityState.Deleted => AuditAction.Deleted,
        _ => null
    };

    private static string ResolveKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;

        var values = key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? string.Empty);

        return string.Join(",", values);
    }

    private static string? SerializeChanges(EntityEntry entry)
    {
        var changes = new Dictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            if (!property.IsModified) continue;

            var name = property.Metadata.Name;
            if (SensitiveProperties.Contains(name)) continue;
            if (name is nameof(IAuditableEntity.ModifiedAt) or nameof(IAuditableEntity.ModifiedBy)) continue;

            var original = property.OriginalValue?.ToString();
            var current = property.CurrentValue?.ToString();
            if (original == current) continue;

            changes[name] = new { old = original, @new = current };
        }

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes);
    }
}
