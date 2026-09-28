namespace CustomerSupportCRM.Domain.Common;

/// <summary>A bilingual reference table a report can group by.
///
/// Exists so one grouping helper serves categories, departments and branches instead of a
/// near-identical copy for each.</summary>
public interface INamedLookup
{
    Guid Id { get; }
    string NameAr { get; }
    string NameEn { get; }
}
