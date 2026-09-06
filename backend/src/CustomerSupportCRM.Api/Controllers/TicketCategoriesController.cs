using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Lookups.Dtos;
using CustomerSupportCRM.Application.Tickets;
using CustomerSupportCRM.Application.Tickets.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Ticket category administration (PDF area 2).
///
/// Deliberately separate from LookupsController: that one is read-only and every picker in
/// the app depends on it, so keeping writes here means a permission change on the lookup
/// route cannot accidentally expose category editing. Writes require lookups.manage, which
/// only Admin and Manager hold.</summary>
[ApiController]
[Route("api/ticket-categories")]
[Authorize(Policy = Permissions.Lookups.Manage)]
public sealed class TicketCategoriesController(ITicketCategoryService categories) : ControllerBase
{
    /// <summary>The full tree including deactivated nodes, which the admin screen needs and
    /// the pickers deliberately do not show.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryLookupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryLookupDto>>> GetTree(
        [FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
        Ok(await categories.GetTreeAsync(includeInactive, ct));

    [HttpPost]
    [ProducesResponseType<CategoryLookupDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CategoryLookupDto>> Create(CategoryUpsertRequest request, CancellationToken ct)
    {
        var created = await categories.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetTree), new { }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CategoryLookupDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CategoryLookupDto>> Update(
        Guid id, CategoryUpsertRequest request, CancellationToken ct) =>
        Ok(await categories.UpdateAsync(id, request, ct));

    /// <summary>Applies a whole drag-and-drop reorder at once, so the tree is never
    /// persisted half-moved.</summary>
    [HttpPost("reorder")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reorder(CategoryReorderRequest request, CancellationToken ct)
    {
        await categories.ReorderAsync(request, ct);
        return NoContent();
    }

    /// <summary>Hides the category from new-ticket pickers. Existing tickets keep it, so
    /// historical reporting does not shift.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await categories.SetActiveAsync(id, false, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await categories.SetActiveAsync(id, true, ct);
        return NoContent();
    }

    /// <summary>Refused while active tickets or sub-categories still reference it.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}
