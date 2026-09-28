using System.Text.Json;
using CustomerSupportCRM.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace CustomerSupportCRM.Api.Middleware;

/// <summary>Turns known application exceptions into RFC 7807 ProblemDetails responses.
/// Anything unrecognised is logged in full and returned as an opaque 500 — internal
/// messages and stack traces must not reach a support agent's browser.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            logger.LogError(exception, "Exception after the response started; cannot write a problem response.");
            throw exception;
        }

        var problem = Translate(context, exception);

        context.Response.Clear();
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>ASP.NET Core ships no constant for the nginx-originated 499.</summary>
    private const int ClientClosedRequest = 499;

    private ProblemDetails Translate(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        switch (exception)
        {
            case ValidationException validation:
            {
                logger.LogInformation("Validation failed on {Path}: {Message}", context.Request.Path, validation.Message);

                var errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                return new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                    Instance = context.Request.Path,
                    Extensions =
                    {
                        ["traceId"] = traceId,
                        ["errorCode"] = ErrorCodes.ValidationFailed
                    }
                };
            }

            case AppException app:
                logger.LogInformation("{ExceptionType} on {Path}: {Message}",
                    app.GetType().Name, context.Request.Path, app.Message);

                return new ProblemDetails
                {
                    Status = app.StatusCode,
                    Title = TitleFor(app.StatusCode),
                    Detail = app.Message,
                    Instance = context.Request.Path,
                    Extensions =
                    {
                        ["traceId"] = traceId,
                        ["errorCode"] = app.ErrorCode
                    }
                };

            case OperationCanceledException:
                logger.LogInformation("Request cancelled: {Path}", context.Request.Path);

                return new ProblemDetails
                {
                    Status = ClientClosedRequest,
                    Title = "Request cancelled.",
                    Instance = context.Request.Path
                };

            default:
                logger.LogError(exception, "Unhandled exception on {Path} (traceId {TraceId})",
                    context.Request.Path, traceId);

                return new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = "Please try again. Quote the trace id if you contact support.",
                    Instance = context.Request.Path,
                    Extensions =
                    {
                        ["traceId"] = traceId,
                        ["errorCode"] = ErrorCodes.Unexpected
                    }
                };
        }
    }

    private static string TitleFor(int statusCode) => statusCode switch
    {
        400 => "Bad request.",
        403 => "Forbidden.",
        404 => "Not found.",
        409 => "Conflict.",
        _ => "Request failed."
    };
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
