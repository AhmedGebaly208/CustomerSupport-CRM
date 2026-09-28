using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using CustomerSupportCRM.Application.Reports;
using CustomerSupportCRM.Application.Reports.Dtos;

namespace CustomerSupportCRM.Infrastructure.Reports;

/// <summary>Turns a report into a downloadable file (PDF area 8).
///
/// Lives in Infrastructure because ClosedXML is a third-party dependency and Application
/// must not take one on to produce a spreadsheet.
///
/// Column headings come from an embedded resource rather than string literals, for the same
/// reason the UI keeps its text in locale files: translations belong in data a translator can
/// edit, not in code.</summary>
public sealed class ReportExporter(IReportsService reports) : IReportExporter
{
    private static readonly Lazy<IReadOnlyDictionary<string, Dictionary<string, string>>> Labels =
        new(LoadLabels);

    public async Task<ExportedFile> ExportAsync(
        ReportExportRequest request, string language, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var arabic = !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        var text = Labels.Value[arabic ? "ar" : "en"];

        var table = await BuildTableAsync(request, text, ct);
        var name = $"{request.Kind}-{DateTime.UtcNow:yyyyMMdd-HHmm}";

        return request.Format == ExportFormat.Csv
            ? new ExportedFile($"{name}.csv", "text/csv", ToCsv(table))
            : new ExportedFile(
                $"{name}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ToXlsx(table, request.Kind.ToString(), arabic));
    }

    private async Task<ReportTable> BuildTableAsync(
        ReportExportRequest request, IReadOnlyDictionary<string, string> text, CancellationToken ct)
        => request.Kind switch
        {
            ReportKind.Tickets => TicketsTable(await reports.GetTicketReportAsync(request.Query, ct), text),
            ReportKind.Sla => SlaTable(await reports.GetSlaReportAsync(request.Query, ct), text),
            ReportKind.Agents => AgentsTable(await reports.GetAgentReportAsync(request.Query, ct), text),
            ReportKind.Csat => CsatTable(await reports.GetCsatReportAsync(request.Query, ct), text),
            _ => new ReportTable([], [])
        };

    private static ReportTable TicketsTable(
        TicketReportDto report, IReadOnlyDictionary<string, string> text) => new(
        [text["period"], text["tickets"]],
        report.Trend
            .Select(b => new[]
            {
                b.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Text(b.Count)
            })
            .ToList());

    private static ReportTable SlaTable(
        SlaReportDto report, IReadOnlyDictionary<string, string> text) => new(
        [text["metric"], text["value"]],
        new List<string[]>
        {
            new[] { text["measured"], Text(report.Measured) },
            new[] { text["firstResponseAttainment"], Text(report.FirstResponseAttainment) },
            new[] { text["resolutionAttainment"], Text(report.ResolutionAttainment) },
            new[] { text["avgFirstResponse"], Text(report.AverageFirstResponseMinutes) },
            new[] { text["medianFirstResponse"], Text(report.MedianFirstResponseMinutes) },
            new[] { text["avgResolution"], Text(report.AverageResolutionMinutes) },
            new[] { text["medianResolution"], Text(report.MedianResolutionMinutes) }
        });

    private static ReportTable AgentsTable(
        AgentReportDto report, IReadOnlyDictionary<string, string> text)
    {
        var arabicNames = ReferenceEquals(text, Labels.Value["ar"]);

        return new ReportTable(
            [
                text["agent"], text["handled"], text["resolved"], text["reopened"],
                text["reopenRate"], text["avgFirstResponse"], text["avgResolution"],
                text["satisfaction"], text["currentLoad"]
            ],
            report.Rows
                .Select(r => new[]
                {
                    arabicNames ? r.NameAr : r.NameEn,
                    Text(r.Handled),
                    Text(r.Resolved),
                    Text(r.Reopened),
                    Text(r.ReopenRate),
                    Text(r.AverageFirstResponseMinutes),
                    Text(r.AverageResolutionMinutes),
                    Text(r.AverageSatisfaction),
                    Text(r.CurrentLoad)
                })
                .ToList());
    }

    private static ReportTable CsatTable(
        CsatReportDto report, IReadOnlyDictionary<string, string> text) => new(
        [text["score"], text["responses"]],
        report.Distribution.Select(d => new[] { d.Key, Text(d.Count) }).ToList());

    /// <summary>An empty cell, not a zero. A missing measurement and a measured zero are
    /// different findings, and a spreadsheet that conflates them misleads.</summary>
    private static string Text(double? value) =>
        value?.ToString("0.#", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static byte[] ToCsv(ReportTable table)
    {
        var builder = new StringBuilder();

        builder.AppendLine(string.Join(',', table.Headers.Select(Escape)));

        foreach (var row in table.Rows)
            builder.AppendLine(string.Join(',', row.Select(Escape)));

        // A BOM, deliberately: Excel opens a CSV in the system codepage without one, which
        // turns every Arabic heading into mojibake. This is the one place a BOM helps.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());

        static string Escape(string value)
        {
            if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n')) return value;

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }

    private static byte[] ToXlsx(ReportTable table, string sheetName, bool arabic)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);

        // The sheet itself flips, so an Arabic export opens reading right to left rather than
        // with the first column stranded on the left.
        sheet.RightToLeft = arabic;

        for (var c = 0; c < table.Headers.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = table.Headers[c];
            cell.Style.Font.Bold = true;
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            for (var c = 0; c < table.Rows[r].Length; c++)
            {
                var value = table.Rows[r][c];
                var cell = sheet.Cell(r + 2, c + 1);

                // Numbers written as numbers, so the spreadsheet can sum and chart them
                // instead of treating a column of figures as text.
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                    cell.Value = number;
                else
                    cell.Value = value;
            }
        }

        sheet.Columns().AdjustToContents();
        if (table.Rows.Count > 0) sheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    private static IReadOnlyDictionary<string, Dictionary<string, string>> LoadLabels()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("report-labels.json", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;

        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
    }

    private sealed record ReportTable(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows);
}
