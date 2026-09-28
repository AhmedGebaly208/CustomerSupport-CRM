using CustomerSupportCRM.Application.Auth;
using CustomerSupportCRM.Application.Reports;
using CustomerSupportCRM.Application.Reports.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Controllers;

/// <summary>Historical reporting (PDF area 8).</summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(
    IReportsService reports,
    IReportExporter exporter) : ControllerBase
{
    [HttpPost("tickets")]
    [Authorize(Permissions.Reports.View)]
    public async Task<ActionResult<TicketReportDto>> Tickets(ReportQuery query, CancellationToken ct) =>
        Ok(await reports.GetTicketReportAsync(query, ct));

    [HttpPost("sla")]
    [Authorize(Permissions.Reports.View)]
    public async Task<ActionResult<SlaReportDto>> Sla(ReportQuery query, CancellationToken ct) =>
        Ok(await reports.GetSlaReportAsync(query, ct));

    /// <summary>Per-agent performance. An agent without the team-view permission may request
    /// only their own row; the service enforces that rather than the route.</summary>
    [HttpPost("agents")]
    [Authorize(Permissions.Dashboard.View)]
    public async Task<ActionResult<AgentReportDto>> Agents(ReportQuery query, CancellationToken ct) =>
        Ok(await reports.GetAgentReportAsync(query, ct));

    [HttpPost("csat")]
    [Authorize(Permissions.Reports.View)]
    public async Task<ActionResult<CsatReportDto>> Csat(ReportQuery query, CancellationToken ct) =>
        Ok(await reports.GetCsatReportAsync(query, ct));

    [HttpPost("export")]
    [Authorize(Permissions.Reports.Export)]
    public async Task<IActionResult> Export(
        ReportExportRequest request, [FromQuery] string? language, CancellationToken ct)
    {
        var file = await exporter.ExportAsync(request, language ?? "ar", ct);

        return File(file.Content, file.ContentType, file.FileName);
    }
}

/// <summary>Customer satisfaction on a resolved ticket. Lives under the ticket route because
/// that is the thing being rated.</summary>
[ApiController]
[Route("api/tickets/{ticketId:guid}/satisfaction")]
[Authorize]
public sealed class SatisfactionController(IReportsService reports) : ControllerBase
{
    [HttpPost]
    [Authorize(Permissions.Tickets.View)]
    public async Task<ActionResult<SatisfactionDto>> Submit(
        Guid ticketId, SubmitSatisfactionRequest request, CancellationToken ct) =>
        Ok(await reports.SubmitSatisfactionAsync(ticketId, request, ct));
}
