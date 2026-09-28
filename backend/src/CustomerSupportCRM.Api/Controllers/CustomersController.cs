using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Exceptions;
using CustomerSupportCRM.Application.Common.Models;
using CustomerSupportCRM.Application.Customers;
using CustomerSupportCRM.Application.Customers.Dtos;
using CustomerSupportCRM.Domain.Enums;
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
    /// <summary>Links or unlinks the portal login for this customer. Gated on user
    /// management rather than customer editing: it grants access, so it belongs with the
    /// permission that governs accounts.</summary>
    [HttpPost("{id:guid}/portal-user")]
    [Authorize(Permissions.Users.Manage)]
    public async Task<ActionResult<CustomerDetailDto>> SetPortalUser(
        Guid id, [FromBody] SetPortalUserRequest request, CancellationToken ct) =>
        Ok(await customers.SetPortalUserAsync(id, request.UserId, ct));

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

    // ---- Attachments (area 1) ----
    //
    // Routed under the customer so the existing scope check gates them; the service
    // resolves ticket and comment owners back to their department the same way.

    [HttpGet("{id:guid}/attachments")]
    [Authorize(Policy = Permissions.Attachments.View)]
    [ProducesResponseType<IReadOnlyList<AttachmentDetailDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AttachmentDetailDto>>> GetAttachments(
        Guid id, CancellationToken ct) =>
        Ok(await customers.ListAttachmentsAsync(AttachmentOwnerType.Customer, id, ct));

    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = Permissions.Attachments.Upload)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<AttachmentDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AttachmentDetailDto>> UploadAttachment(
        Guid id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BadRequestException("No file was uploaded.");

        await using var stream = file.OpenReadStream();

        var attachment = await customers.AddAttachmentAsync(
            AttachmentOwnerType.Customer, id, stream, file.FileName, file.ContentType, ct);

        return CreatedAtAction(nameof(GetAttachments), new { id }, attachment);
    }

    /// <summary>Streams the file back under its original name and content type.</summary>
    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Policy = Permissions.Attachments.View)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var (content, contentType, fileName) =
            await customers.OpenAttachmentAsync(AttachmentOwnerType.Customer, id, attachmentId, ct);

        return File(content, contentType, fileName);
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Policy = Permissions.Attachments.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        await customers.DeleteAttachmentAsync(AttachmentOwnerType.Customer, id, attachmentId, ct);
        return NoContent();
    }

    // ---- Merge ----

    /// <summary>Folds one customer into another. Everything the loser owns moves to the
    /// survivor in a single transaction, and the loser is soft-deleted with an audit entry
    /// naming where its history went.</summary>
    [HttpPost("merge")]
    [Authorize(Policy = Permissions.Customers.Merge)]
    [ProducesResponseType<CustomerMergeResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerMergeResultDto>> Merge(
        CustomerMergeRequest request, CancellationToken ct) =>
        Ok(await customers.MergeAsync(request, ct));

    // ---- Bulk import ----

    /// <summary>Imports customers from a .csv or .xlsx file. Every row is reported
    /// individually; one bad row never aborts the rest.</summary>
    [HttpPost("import")]
    [Authorize(Policy = Permissions.Customers.Import)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<CustomerImportResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerImportResultDto>> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw new BadRequestException("No file was uploaded.");

        // Buffered to a MemoryStream because ClosedXML needs a seekable stream, and the
        // upload size is already capped by the request limits.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        return Ok(await customers.ImportAsync(buffer, file.FileName, file.ContentType, ct));
    }

    // ---- Activity timeline ----

    /// <summary>One chronological feed interleaving tickets, interactions, notes and
    /// attachments for this customer.</summary>
    [HttpGet("{id:guid}/activity")]
    [ProducesResponseType<PagedResult<CustomerActivityItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CustomerActivityItemDto>>> GetActivity(
        Guid id, [FromQuery] CustomerActivityQuery query, CancellationToken ct) =>
        Ok(await customers.GetActivityAsync(id, query, ct));

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
