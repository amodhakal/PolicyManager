using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;

namespace PolicyManager.Middleware;

/// <summary>
///     Rejects an oversized request body before any handler runs.
/// </summary>
/// <remarks>
///     <para>
///     Kestrel enforces a body limit too, and that is the real defence: it stops reading while the
///     payload is still arriving, so a hostile upload never occupies memory. This middleware exists
///     for the two things Kestrel's limit does not give us. It produces the same RFC 9457
///     ProblemDetails as every other failure, carrying the correlation ID, instead of Kestrel's bare
///     connection reset; and it runs identically under <c>TestServer</c>, where no Kestrel limit
///     applies and the behaviour would otherwise be untestable.
///     </para>
///     <para>
///     It checks <c>Content-Length</c>, so it is a fast rejection rather than a defence: a chunked
///     request that declares no length passes here and is caught by the server limit instead. That
///     division is deliberate — measuring a stream to discover it is too long means reading all of
///     it, which is the thing being prevented.
///     </para>
/// </remarks>
public class RequestSizeLimitMiddleware(RequestDelegate next, ILogger<RequestSizeLimitMiddleware> logger)
{
    /// <summary>
    ///     Runs the rest of the pipeline unless the declared body length is over the limit.
    /// </summary>
    /// <param name="context">The current request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = context.RequestServices
            .GetRequiredService<IOptions<RateLimitOptions>>().Value.MaxRequestBodySizeBytesOrNull;

        if (limit is null || !IsOverLimit(context, limit.Value))
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "Rejected a request declaring {Declared} bytes, over the {Limit} byte limit.",
            context.Request.ContentLength,
            limit.Value);

        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;

        // Written through the shared service rather than serialised here, so the content type and the
        // correlation-ID extension match every other failure in the API. A failure that answers in a
        // different shape from the rest is one a client has to special-case.
        var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();

        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status413PayloadTooLarge,
                Title = "Payload too large",
                Detail = "The request body is larger than this endpoint accepts.",
                Type = "https://httpstatuses.com/413",
                Instance = context.Request.Path
            }
        });
    }

    /// <summary>
    ///     Whether the request declares a body over the limit.
    /// </summary>
    /// <remarks>
    ///     A negative <c>Content-Length</c> is the chunked form, which declares no length at all and
    ///     is left to the server limit.
    /// </remarks>
    private static bool IsOverLimit(HttpContext context, long limit)
        => context.Request.ContentLength is { } declared && declared > limit;
}
