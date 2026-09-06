using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Customers;
using CustomerSupportCRM.Application.Customers.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Customer management (PDF area 1).
///
/// Authorised by permission, not role. Previous role mapping, preserved exactly:
/// Staff (Admin/Manager/Agent) had full read+write; Supervisory (Admin/Manager) alone
/// could delete. See <see cref="RolePermissions"/> for the role -> permission sets.</summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = Permissions.Customers.View)]
public sealed class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CustomerListItemDto>>> Search(
        [FromQuery] CustomerQuery query, CancellationToken ct) =>
        Ok(await customers.SearchAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> GetById(Guid id, CancellationToken ct) =>
        Ok(await customers.GetByIdAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Permissions.Customers.Create)]
    [ProducesResponseType<CustomerDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDetailDto>> Create(CreateCustomerRequest request, CancellationToken ct)
    {
        var created = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.Customers.Edit)]
    [ProducesResponseType<CustomerDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerDetailDto>> Update(Guid id, UpdateCustomerRequest request, CancellationToken ct) =>
        Ok(await customers.UpdateAsync(id, request, ct));

    /// <summary>Soft-deletes the customer. Refused while they still have active tickets.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Permissions.Customers.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---- Notes ----

    [HttpGet("{id:guid}/notes")]
    [ProducesResponseType<IReadOnlyList<CustomerNoteDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CustomerNoteDto>>> GetNotes(Guid id, CancellationToken ct) =>
        Ok(await customers.GetNotesAsync(id, ct));

    [HttpPost("{id:guid}/notes")]
    [Authorize(Policy = Permissions.Customers.Edit)]
    [ProducesResponseType<CustomerNoteDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerNoteDto>> AddNote(Guid id, CreateCustomerNoteRequest request, CancellationToken ct)
    {
        var note = await customers.AddNoteAsync(id, request, ct);
        return CreatedAtAction(nameof(GetNotes), new { id }, note);
    }

    [HttpDelete("{id:guid}/notes/{noteId:guid}")]
    [Authorize(Policy = Permissions.Customers.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteNote(Guid id, Guid noteId, CancellationToken ct)
    {
        await customers.DeleteNoteAsync(id, noteId, ct);
        return NoContent();
    }

    // ---- Interaction history ----

    [HttpGet("{id:guid}/interactions")]
    [ProducesResponseType<PagedResult<InteractionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InteractionDto>>> GetInteractions(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await customers.GetInteractionsAsync(id, page, pageSize, ct));

    [HttpPost("{id:guid}/interactions")]
    [Authorize(Policy = Permissions.Customers.Edit)]
    [ProducesResponseType<InteractionDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<InteractionDto>> AddInteraction(
        Guid id, CreateInteractionRequest request, CancellationToken ct)
    {
        var interaction = await customers.AddInteractionAsync(id, request, ct);
        return CreatedAtAction(nameof(GetInteractions), new { id }, interaction);
    }
}
