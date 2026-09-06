using CustomerSupportCRM.Application.AuditLogs;
using CustomerSupportCRM.Application.AuditLogs.Dtos;
using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>The audit trail (PDF area 10 "Audit logs").
///
/// Read-only by design: only GET verbs exist here, and the underlying rows are written
/// solely by the persistence interceptor. Adding a write verb to this controller would
/// destroy the trail's value as evidence, so there is deliberately no create, edit or
/// delete endpoint — not even for an administrator.</summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = Permissions.AuditLogs.View)]
public sealed class AuditLogsController(IAuditLogService auditLogs) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
        [FromQuery] AuditLogQuery query, CancellationToken ct) =>
        Ok(await auditLogs.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AuditLogDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AuditLogDto>> GetById(Guid id, CancellationToken ct) =>
        Ok(await auditLogs.GetAsync(id, ct));

    /// <summary>Distinct entity names in the trail, for the filter dropdown.</summary>
    [HttpGet("facets")]
    [ProducesResponseType<AuditLogFacetsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditLogFacetsDto>> Facets(CancellationToken ct) =>
        Ok(await auditLogs.GetFacetsAsync(ct));
}
