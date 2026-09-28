namespace CustomerSupportCRM.Application.Common.Exceptions;

/// <summary>Base for exceptions the API translates into a meaningful HTTP status
/// instead of a 500. Anything not derived from this is a genuine bug.</summary>
public abstract class AppException(string message, string errorCode) : Exception(message)
{
    public abstract int StatusCode { get; }

    /// <summary>Stable, language-neutral identifier for this failure, returned to the client
    /// as `errorCode` on the problem response.
    ///
    /// The message carried here is English and is meant for logs and for clients with no
    /// translation for the code. A localised UI looks the code up in its own resources, so
    /// the server never decides what language the user reads.</summary>
    public string ErrorCode { get; } = errorCode;
}

public sealed class NotFoundException(string entity, object key, string errorCode = ErrorCodes.NotFound)
    : AppException($"{entity} '{key}' was not found.", errorCode)
{
    public override int StatusCode => 404;
}

/// <summary>A request that is well-formed but conflicts with current state — a duplicate
/// code, or a ticket status transition the workflow does not allow.</summary>
public sealed class ConflictException(string message, string errorCode = ErrorCodes.Conflict)
    : AppException(message, errorCode)
{
    public override int StatusCode => 409;
}

public sealed class ForbiddenException(string message, string errorCode = ErrorCodes.Forbidden)
    : AppException(message, errorCode)
{
    public override int StatusCode => 403;
}

public sealed class BadRequestException(string message, string errorCode = ErrorCodes.BadRequest)
    : AppException(message, errorCode)
{
    public override int StatusCode => 400;
}

/// <summary>Error codes the client translates. A call site that does not pass one falls back
/// to the generic code for its status, which the client renders as a neutral message — so an
/// untranslated failure is still readable, just less specific.
///
/// Codes are added here as screens are localised; the English text on the exception stays
/// correct in the meantime.</summary>
public static class ErrorCodes
{
    // Generic fallbacks, one per status.
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";
    public const string Forbidden = "forbidden";
    public const string BadRequest = "bad-request";

    // Authentication.
    public const string InvalidCredentials = "auth.invalid-credentials";
    public const string InvalidRefreshToken = "auth.invalid-refresh-token";
    public const string NotAuthenticated = "auth.not-authenticated";

    // Customers.
    public const string DuplicateCustomerEmail = "customers.duplicate-email";

    // SLA.
    public const string SlaPolicyInUse = "sla.policy-in-use";

    // Knowledge base.
    public const string ArticleIncomplete = "kb.article-incomplete";
    public const string ArticleInUse = "kb.article-in-use";

    // Surfaced by the middleware rather than by a call site.
    public const string ValidationFailed = "validation-failed";
    public const string Unexpected = "unexpected";
}
