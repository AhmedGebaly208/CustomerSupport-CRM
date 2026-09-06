namespace CustomerSupportCRM.Application.Common.Exceptions;

/// <summary>Base for exceptions the API translates into a meaningful HTTP status
/// instead of a 500. Anything not derived from this is a genuine bug.</summary>
public abstract class AppException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }
}

public sealed class NotFoundException(string entity, object key)
    : AppException($"{entity} '{key}' was not found.")
{
    public override int StatusCode => 404;
}

/// <summary>A request that is well-formed but conflicts with current state — a duplicate
/// code, or a ticket status transition the workflow does not allow.</summary>
public sealed class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => 409;
}

public sealed class ForbiddenException(string message) : AppException(message)
{
    public override int StatusCode => 403;
}

public sealed class BadRequestException(string message) : AppException(message)
{
    public override int StatusCode => 400;
}
