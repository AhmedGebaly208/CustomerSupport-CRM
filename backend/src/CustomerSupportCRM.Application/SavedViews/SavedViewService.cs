using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Tickets.Dtos;
using CustomerSupportCRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportCRM.Application.SavedViews;

/// <summary>Personal saved filter combinations (PDF area 2).
///
/// Every method is scoped to the calling user: a saved view is a private convenience, not
/// shared configuration, so the caller's id is taken from the token and never from the
/// request.</summary>
public interface ISavedViewService
{
    Task<IReadOnlyList<SavedViewDto>> ListAsync(string entityKind, CancellationToken ct = default);
    Task<SavedViewDto> UpsertAsync(UpsertSavedViewRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class SavedViewService(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ISavedViewService
{
    public async Task<IReadOnlyList<SavedViewDto>> ListAsync(
        string entityKind, CancellationToken ct = default)
    {
        var userId = RequireCaller();
        var kind = Normalize(entityKind);

        return await db.UserSavedViews.AsNoTracking()
            .Where(v => v.UserId == userId && v.EntityKind == kind)
            .OrderBy(v => v.Name)
            .Select(v => new SavedViewDto(v.Id, v.Name, v.EntityKind, v.FiltersJson))
            .ToListAsync(ct);
    }

    /// <summary>Upserts by name: saving a view with an existing name replaces it, which is
    /// what "save" means to someone refining a filter they already keep.</summary>
    public async Task<SavedViewDto> UpsertAsync(
        UpsertSavedViewRequest request, CancellationToken ct = default)
    {
        var userId = RequireCaller();
        var kind = Normalize(request.EntityKind);
        var name = request.Name.Trim();

        var view = await db.UserSavedViews
            .FirstOrDefaultAsync(v => v.UserId == userId && v.EntityKind == kind && v.Name == name, ct);

        if (view is null)
        {
            view = new UserSavedView { UserId = userId, EntityKind = kind, Name = name };
            db.UserSavedViews.Add(view);
        }

        view.FiltersJson = request.FiltersJson;

        await db.SaveChangesAsync(ct);
        return new SavedViewDto(view.Id, view.Name, view.EntityKind, view.FiltersJson);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireCaller();

        var view = await db.UserSavedViews.FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NotFoundException(nameof(UserSavedView), id);

        // Someone else's saved view is not the caller's to delete, even for an admin —
        // there is nothing operational to gain and it would surprise the owner.
        if (view.UserId != userId)
            throw new ForbiddenException("That saved view belongs to another user.");

        view.IsDeleted = true;
        view.DeletedAt = clock.UtcNow;
        view.DeletedBy = userId;

        await db.SaveChangesAsync(ct);
    }

    private Guid RequireCaller() =>
        currentUser.UserId ?? throw new ForbiddenException("Not authenticated.");

    private static string Normalize(string? entityKind) =>
        string.IsNullOrWhiteSpace(entityKind) ? "Ticket" : entityKind.Trim();
}
