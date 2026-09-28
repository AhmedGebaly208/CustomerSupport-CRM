using CustomerSupportCRM.Application.Reports.Dtos;

namespace CustomerSupportCRM.Application.Reports;

public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);

/// <summary>Renders a report as a downloadable file. Implemented in Infrastructure, because
/// producing a spreadsheet needs a third-party library that Application must not take on.</summary>
public interface IReportExporter
{
    Task<ExportedFile> ExportAsync(
        ReportExportRequest request, string language, CancellationToken ct = default);
}
