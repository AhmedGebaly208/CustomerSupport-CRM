namespace CustomerSupportCRM.Application.Lookups.Dtos;

/// <summary>Bilingual option for dropdowns; the frontend picks the field matching the
/// active locale (area 12).</summary>
public sealed record LookupDto(Guid Id, string NameAr, string NameEn, string? Code = null);

public sealed record CategoryLookupDto(
    Guid Id,
    string NameAr,
    string NameEn,
    Guid? ParentId,
    Guid? DepartmentId,
    int SortOrder,
    IReadOnlyList<CategoryLookupDto> Children);

/// <summary>Enum option surfaced to the client so status/priority pickers stay in sync
/// with the server without duplicating the enum in TypeScript.</summary>
public sealed record EnumOptionDto(int Value, string Name);
