using CustomerSupportCRM.Application.Common.Models;

namespace CustomerSupportCRM.Application.Auth.Dtos;

/// <summary>A user as the administration screens see them. Never expose
/// <c>ApplicationUser</c> itself — it carries the password hash and refresh token.</summary>
public sealed record UserAdminDto(
    Guid Id,
    string Email,
    string FullNameAr,
    string FullNameEn,
    string PreferredLanguage,
    Guid? DepartmentId,
    string? DepartmentNameAr,
    string? DepartmentNameEn,
    Guid? BranchId,
    string? BranchNameAr,
    string? BranchNameEn,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(
    string Email,
    string Password,
    string FullNameAr,
    string FullNameEn,
    string? PreferredLanguage,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(
    string FullNameAr,
    string FullNameEn,
    string? PreferredLanguage,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<string> Roles);

public sealed record SetUserRolesRequest(IReadOnlyList<string> Roles);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Inherits Page/PageSize/Search/SortBy/SortDescending from <see cref="PagedQuery"/>;
/// Search matches email and both name languages.</summary>
public sealed class UserListQuery : PagedQuery
{
    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }
    public string? Role { get; set; }
    public bool? IsActive { get; set; }
}
