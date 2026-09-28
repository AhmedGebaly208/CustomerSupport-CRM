using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CustomerSupportCRM.Application.Common.Interfaces;
using CustomerSupportCRM.Application.Customers.Dtos;

namespace CustomerSupportCRM.Infrastructure.Import;

/// <summary>Column names an import file may use, in either language.
///
/// Matching on header names rather than column position means an operator can reorder or
/// omit columns, and an Arabic-speaking operator can export from their own tooling without
/// renaming anything first.</summary>
internal static class ImportColumns
{
    public static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FullNameAr"] = ["fullnamear", "namear", "arabicname", "الاسم بالعربية", "الاسم العربي", "الاسم"],
            ["FullNameEn"] = ["fullnameen", "nameen", "englishname", "name", "الاسم بالإنجليزية", "الاسم الانجليزي"],
            ["Email"] = ["email", "e-mail", "mail", "البريد الإلكتروني", "البريد"],
            ["Phone"] = ["phone", "mobile", "telephone", "الجوال", "الهاتف", "رقم الجوال"],
            ["WhatsAppNumber"] = ["whatsapp", "whatsappnumber", "واتساب", "رقم واتساب"],
            ["CompanyName"] = ["company", "companyname", "organisation", "organization", "الشركة", "الجهة"],
            ["NationalId"] = ["nationalid", "idnumber", "iqama", "الهوية", "رقم الهوية"],
            ["Address"] = ["address", "العنوان"],
            ["PreferredLanguage"] = ["language", "preferredlanguage", "locale", "اللغة"],
            ["DepartmentCode"] = ["department", "departmentcode", "القسم", "رمز القسم"],
            ["BranchCode"] = ["branch", "branchcode", "الفرع", "رمز الفرع"],
        };

    /// <summary>Maps a sheet's header row to field names, ignoring anything unrecognised.</summary>
    public static Dictionary<int, string> MapHeaders(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<int, string>();

        for (var i = 0; i < headers.Count; i++)
        {
            var header = Normalize(headers[i]);
            if (header.Length == 0) continue;

            foreach (var (field, aliases) in Aliases)
            {
                if (aliases.Any(a => Normalize(a) == header))
                {
                    map[i] = field;
                    break;
                }
            }
        }

        return map;
    }

    /// <summary>Strips spaces, underscores and hyphens so "Full Name (EN)" and "full_name_en"
    /// both match the same alias.</summary>
    private static string Normalize(string value) =>
        new(value.Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != '(' && c != ')').ToArray());

    public static CustomerImportRow ToRow(int rowNumber, Dictionary<int, string> headerMap, IReadOnlyList<string> cells)
    {
        string? Get(string field)
        {
            foreach (var (index, name) in headerMap)
            {
                if (name != field || index >= cells.Count) continue;
                var value = cells[index]?.Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        return new CustomerImportRow(
            rowNumber,
            Get("FullNameAr"), Get("FullNameEn"), Get("Email"), Get("Phone"),
            Get("WhatsAppNumber"), Get("CompanyName"), Get("NationalId"), Get("Address"),
            Get("PreferredLanguage"), Get("DepartmentCode"), Get("BranchCode"));
    }
}

/// <summary>CSV import.
///
/// A hand-rolled reader rather than a CSV library: the format needed here is small and
/// well defined (quoted fields, doubled quotes, embedded newlines), and one fewer
/// dependency is worth more than the edge cases a full library would also handle.</summary>
public sealed class CsvCustomerImportParser : ICustomerImportParser
{
    public bool CanParse(string fileName, string? contentType) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
        || contentType is "text/csv" or "application/csv";

    public async Task<IReadOnlyList<CustomerImportRow>> ParseAsync(
        Stream content, CancellationToken cancellationToken = default)
    {
        // detectEncodingFromByteOrderMarks handles the BOM Excel writes on "CSV UTF-8",
        // without which the first header would carry an invisible prefix and never match.
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var text = await reader.ReadToEndAsync(cancellationToken);
        var records = SplitRecords(text);

        if (records.Count == 0) return [];

        var headerMap = ImportColumns.MapHeaders(records[0]);
        if (headerMap.Count == 0) return [];

        var rows = new List<CustomerImportRow>();

        for (var i = 1; i < records.Count; i++)
        {
            // Skip blank lines rather than reporting them as failed rows; a trailing
            // newline is not an error the operator needs to see.
            if (records[i].All(string.IsNullOrWhiteSpace)) continue;

            // +1 so the number matches what the operator sees in their spreadsheet.
            rows.Add(ImportColumns.ToRow(i + 1, headerMap, records[i]));
        }

        return rows;
    }

    private static List<List<string>> SplitRecords(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote inside a quoted field is a literal quote.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    break;

                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(fields);
                    fields = [];
                    break;

                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields);
        }

        return records;
    }
}

/// <summary>XLSX import, reading the first worksheet.</summary>
public sealed class XlsxCustomerImportParser : ICustomerImportParser
{
    public bool CanParse(string fileName, string? contentType) =>
        fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
        || contentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public Task<IReadOnlyList<CustomerImportRow>> ParseAsync(
        Stream content, CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook(content);

        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet is null) return Task.FromResult<IReadOnlyList<CustomerImportRow>>([]);

        var used = sheet.RangeUsed();
        if (used is null) return Task.FromResult<IReadOnlyList<CustomerImportRow>>([]);

        var sheetRows = used.RowsUsed().ToList();
        if (sheetRows.Count == 0) return Task.FromResult<IReadOnlyList<CustomerImportRow>>([]);

        var headers = sheetRows[0].Cells().Select(c => c.GetFormattedString()).ToList();
        var headerMap = ImportColumns.MapHeaders(headers);

        if (headerMap.Count == 0) return Task.FromResult<IReadOnlyList<CustomerImportRow>>([]);

        var rows = new List<CustomerImportRow>();

        for (var i = 1; i < sheetRows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // GetFormattedString, not GetString: a phone number stored as a number would
            // otherwise arrive as "5.55123E+08".
            var cells = sheetRows[i].Cells(1, headers.Count)
                .Select(c => c.GetFormattedString().Trim())
                .ToList();

            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            rows.Add(ImportColumns.ToRow(
                sheetRows[i].RowNumber(), headerMap, cells));
        }

        return Task.FromResult<IReadOnlyList<CustomerImportRow>>(rows);
    }
}
