using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.SavedViews;
using CustomerSupportCRM.Application.Tickets.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Personal saved filter combinations (PDF area 2).
///
/// No user id appears in any route or payload: every operation is scoped to the caller's
/// token, so one agent's saved views are unreachable from another's session by construction
/// rather than by a check that could be forgotten.</summary>
[ApiController]
[Route("api/saved-views")]
[Authorize(Policy = Permissions.Tickets.View)]
public sealed class SavedViewsController(ISavedViewService savedViews) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SavedViewDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SavedViewDto>>> List(
        [FromQuery] string entityKind = "Ticket", CancellationToken ct = default) =>
        Ok(await savedViews.ListAsync(entityKind, ct));

    /// <summary>Upsert by name: saving over an existing name replaces it, which is what
    /// "save" means to someone refining a filter they already keep.</summary>
    [HttpPost]
    [ProducesResponseType<SavedViewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavedViewDto>> Upsert(UpsertSavedViewRequest request, CancellationToken ct) =>
        Ok(await savedViews.UpsertAsync(request, ct));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await savedViews.DeleteAsync(id, ct);
        return NoContent();
    }
}
