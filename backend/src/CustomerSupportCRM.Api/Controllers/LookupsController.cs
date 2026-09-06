using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Lookups;
using CustomerSupportCRM.Application.Lookups.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

[ApiController]
[Route("api/lookups")]
[Authorize(Policy = Permissions.Lookups.View)]
public sealed class LookupsController(ILookupService lookups) : ControllerBase
{
    [HttpGet("departments")]
    [ProducesResponseType<IReadOnlyList<LookupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> Departments(CancellationToken ct) =>
        Ok(await lookups.GetDepartmentsAsync(ct));

    [HttpGet("branches")]
    [ProducesResponseType<IReadOnlyList<LookupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LookupDto>>> Branches(CancellationToken ct) =>
        Ok(await lookups.GetBranchesAsync(ct));

    /// <summary>Category tree, optionally narrowed to a department (department-less
    /// categories are always included, since they apply everywhere).</summary>
    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<CategoryLookupDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryLookupDto>>> Categories(
        [FromQuery] Guid? departmentId, CancellationToken ct) =>
        Ok(await lookups.GetCategoryTreeAsync(departmentId, ct));

    /// <summary>Server-owned enum options, so the client pickers cannot drift.</summary>
    [HttpGet("enums")]
    [ProducesResponseType<IReadOnlyDictionary<string, IReadOnlyList<EnumOptionDto>>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyDictionary<string, IReadOnlyList<EnumOptionDto>>> Enums() =>
        Ok(lookups.GetEnums());
}
