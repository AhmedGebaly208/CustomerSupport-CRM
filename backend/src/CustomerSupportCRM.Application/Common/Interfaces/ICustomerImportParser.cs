using CustomerSupportCRM.Application.Customers.Dtos;

namespace CustomerSupportCRM.Application.Common.Interfaces;

/// <summary>Reads customer rows out of an uploaded file.
///
/// The interface lives in Application but every implementation lives in Infrastructure,
/// because a spreadsheet format is an external detail in exactly the same way a database
/// or a file system is. Application never references a parsing library.</summary>
public interface ICustomerImportParser
{
    /// <summary>True when this parser handles the given file. Matched on extension and
    /// content type, because browsers are inconsistent about the latter.</summary>
    bool CanParse(string fileName, string? contentType);

    /// <summary>Reads every row. Parsing problems on a single row are reported as an empty
    /// or partial row rather than an exception, so one malformed line cannot abort an
    /// otherwise good import — per-row validation happens afterwards in the service.</summary>
    Task<IReadOnlyList<CustomerImportRow>> ParseAsync(Stream content, CancellationToken cancellationToken = default);
}
