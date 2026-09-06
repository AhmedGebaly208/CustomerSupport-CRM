namespace CustomerSupportCRM.Application.Auth.Dtos;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    CurrentUserDto User);

/// <summary>The signed-in caller. <c>Permissions</c> is resolved server-side and returned
/// here rather than decoded from the JWT on the client, so the UI and the API can never
/// disagree about what the user is allowed to do.</summary>
public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FullNameAr,
    string FullNameEn,
    string PreferredLanguage,
    Guid? DepartmentId,
    Guid? BranchId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

/// <summary>An assignable agent, for the ticket-assignment picker.</summary>
public sealed record AgentDto(
    Guid Id,
    string FullNameAr,
    string FullNameEn,
    string Email,
    Guid? DepartmentId,
    int OpenTicketCount);
