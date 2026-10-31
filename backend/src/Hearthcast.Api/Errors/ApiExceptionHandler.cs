using Hearthcast.Data.Validation;
using Microsoft.AspNetCore.Diagnostics;

namespace Hearthcast.Api.Errors;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            Hearthcast.Data.Snapshots.SnapshotConflictException => (409, "Snapshot revision conflict", exception.Message),
            BusinessRuleViolationException => (409, "Operation conflicts with current state", exception.Message),
            ResourceNotFoundException => (404, "Resource not found", exception.Message),
            InvalidValueException => (400, "Invalid request", "One or more supplied values are invalid."),
            BadHttpRequestException bad => (bad.StatusCode, "Invalid request", "The request could not be processed."),
            _ => (500, "An unexpected error occurred", "The request could not be completed.")
        };
        if (status == 500)
        {
            // Keep server-side diagnostics separate from the public error response.
            logger.LogError(exception, "Unhandled API error. Trace ID: {TraceId}", context.TraceIdentifier);
        }
        context.Response.StatusCode = status;
        await Results.Problem(statusCode: status, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }
}
