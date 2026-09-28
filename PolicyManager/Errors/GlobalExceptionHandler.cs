using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Exceptions;
using PolicyManager.Middleware;

namespace PolicyManager.Errors;

/// <summary>
///     Turns any unhandled exception into a ProblemDetails response.
/// </summary>
/// <remarks>
///     Without this, a <see cref="DbUpdateException" /> escapes as a bare 500 with an empty body and
///     no correlation identifier, so the caller learns nothing and support has nothing to search on.
///     Each exception is mapped to the status that actually describes it: a missing entity is a 404, a
///     duplicate is a 409 the caller can resolve, a broken domain rule is a 422, and only genuinely
///     unexpected faults are a 500. The correlation identifier is attached to every response so a
///     reported failure can be tied back to the logs.
/// </remarks>
public class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    ///     Non-standard status for a request that was cancelled rather than completed.
    /// </summary>
    private const int RequestCancelled = StatusCodes.Status499ClientClosedRequest;

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // The caller has already gone, so there is nobody to read a response. Writing to an aborted
        // response body throws, which would escape the handler as a second, noisier failure on top
        // of the first. Log and claim the exception as handled.
        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request abandoned by the client. Correlation ID: {CorrelationId}",
                CorrelationId(httpContext));

            return true;
        }

        var problem = Classify(httpContext, exception);

        if (problem.Status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception. Correlation ID: {CorrelationId}", CorrelationId(httpContext));
        else
            logger.LogWarning(
                exception,
                "Request rejected with {StatusCode}. Correlation ID: {CorrelationId}",
                problem.Status,
                CorrelationId(httpContext));

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    /// <summary>
    ///     Maps an exception to the ProblemDetails that describes it.
    /// </summary>
    private static ProblemDetails Classify(HttpContext httpContext, Exception exception)
    {
        var problem = exception switch
        {
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource not found",
                Detail = notFound.Message,
                Type = "https://httpstatuses.com/404"
            },

            ConflictException conflict => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = conflict.Message,
                Type = "https://httpstatuses.com/409"
            },

            BusinessRuleException rule => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Business rule violation",
                Detail = rule.Message,
                Type = "https://httpstatuses.com/422",
                Extensions = rule.Rule is null
                    ? null
                    : new Dictionary<string, object?> { ["rule"] = rule.Rule }
            },

            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "Invalid request",
                Detail = badRequest.Message,
                Type = "https://httpstatuses.com/400"
            },

            DbUpdateException dbUpdate => ClassifyDbUpdate(dbUpdate),

            // Only reachable for a cancellation the server caused, such as an EF command timeout:
            // a genuine client abort is intercepted by the framework before the handler runs. Calling
            // it "client closed request" would misattribute the cause, so it is named for what it is.
            OperationCanceledException => new ProblemDetails
            {
                Status = RequestCancelled,
                Title = "Request cancelled"
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",
                Type = "https://httpstatuses.com/500"
            }
        };

        // The exception message can name columns, values or SQL fragments, so it is deliberately
        // not echoed to the caller for server faults. It stays in the logs, keyed by correlation id.
        if (problem.Status >= StatusCodes.Status500InternalServerError && problem.Detail is null)
            problem.Detail = "The request could not be completed. Quote the correlation ID when reporting this.";

        problem.Instance = httpContext.Request.Path;

        if (httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId))
            problem.Extensions["correlationId"] = correlationId;

        return problem;
    }

    /// <summary>
    ///     Maps a failed write onto the constraint that actually rejected it.
    /// </summary>
    private static ProblemDetails ClassifyDbUpdate(DbUpdateException exception)
    {
        if (SqlErrorTranslator.IsUniqueViolation(exception))
        {
            var constraint = SqlErrorTranslator.GetConstraintName(exception);

            return new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = constraint is null
                    ? "A record with the same unique value already exists."
                    : $"A record violating '{constraint}' already exists.",
                Type = "https://httpstatuses.com/409"
            };
        }

        if (SqlErrorTranslator.IsForeignKeyViolation(exception))
        {
            var constraint = SqlErrorTranslator.GetConstraintName(exception);

            return new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Related record does not exist",
                Detail = constraint is null
                    ? "The request references a record that does not exist."
                    : $"The request violates the foreign key '{constraint}'.",
                Type = "https://httpstatuses.com/409"
            };
        }

        if (SqlErrorTranslator.IsColumnLimitViolation(exception))
        {
            return new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Value out of range",
                Detail = "A value supplied does not fit the column it would be stored in.",
                Type = "https://httpstatuses.com/400"
            };
        }

        // Anything else reaching here is not a constraint the caller caused or can fix: a deadlock,
        // a command timeout, a dropped connection, a NOT NULL violation. Reporting it as a 409 would
        // tell the caller their request is the problem, and would keep it at Warning severity where
        // it never reaches the alerting that a genuine server fault should trip.
        return new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred",
            Detail = "The request could not be completed. Quote the correlation ID when reporting this.",
            Type = "https://httpstatuses.com/500"
        };
    }

    private static string CorrelationId(HttpContext httpContext)
        => httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) && value is string id
            ? id
            : httpContext.TraceIdentifier;
}
