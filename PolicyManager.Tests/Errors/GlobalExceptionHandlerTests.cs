using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PolicyManager.Errors;
using PolicyManager.Exceptions;
using PolicyManager.Middleware;

namespace PolicyManager.Tests.Errors;

/// <summary>
///     Tests for how <see cref="GlobalExceptionHandler" /> maps an exception to a status and a body.
/// </summary>
/// <remarks>
///     Exercised directly rather than through the HTTP pipeline so each mapping can be pinned
///     individually. Only exceptions that can actually be constructed are covered here: the
///     <c>DbUpdateException</c> mappings depend on a real <c>SqlException</c>, which cannot be
///     constructed in-process, so those are asserted against a live SQL Server in
///     <c>SqlServerProblemDetailsTests</c>.
/// </remarks>
public class GlobalExceptionHandlerTests
{
    /// <summary>
    ///     A missing entity is a 404.
    /// </summary>
    [Fact]
    public async Task Not_found_exception_becomes_404()
    {
        var (problem, _) = await HandleAsync(new NotFoundException("Policy", 42));

        Assert.Equal(404, problem.Status);
        Assert.Equal("Resource not found", problem.Title);
        Assert.Contains("42", problem.Detail);
    }

    /// <summary>
    ///     A uniqueness conflict is a 409 the caller can resolve.
    /// </summary>
    [Fact]
    public async Task Conflict_exception_becomes_409()
    {
        var (problem, _) = await HandleAsync(new ConflictException("Email already registered."));

        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
    }

    /// <summary>
    ///     A broken domain rule is a 422, distinct from a 409 conflict.
    /// </summary>
    /// <remarks>
    ///     The split matters to a client: retrying a 422 unchanged fails again, whereas a 409 may
    ///     succeed after the conflicting state is dealt with.
    /// </remarks>
    [Fact]
    public async Task Business_rule_exception_becomes_422()
    {
        var (problem, _) = await HandleAsync(new BusinessRuleException("Policy is not active."));

        Assert.Equal(422, problem.Status);
        Assert.Equal("Business rule violation", problem.Title);
    }

    /// <summary>
    ///     An unrecognised failure is a 500 and does not echo the exception message.
    /// </summary>
    /// <Fact]
    public async Task Unhandled_exception_becomes_a_sanitised_500()
    {
        var (problem, _) = await HandleAsync(
            new InvalidOperationException("failed to reach srv-secret-db-host with password=hunter2"));

        Assert.Equal(500, problem.Status);
        Assert.DoesNotContain("hunter2", problem.Detail);
        Assert.DoesNotContain("srv-secret-db-host", problem.Detail);
        Assert.Contains("correlation ID", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A malformed request keeps the status the framework assigned it.
    /// </summary>
    [Fact]
    public async Task Bad_request_exception_keeps_its_status()
    {
        var (problem, _) = await HandleAsync(new BadHttpRequestException("Malformed JSON.", 400));

        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid request", problem.Title);
    }

    /// <summary>
    ///     A cancelled request is reported as 499 rather than a server fault.
    /// </summary>
    [Fact]
    public async Task Operation_cancelled_becomes_499()
    {
        var (problem, _) = await HandleAsync(new OperationCanceledException());

        Assert.Equal(499, problem.Status);
    }

    /// <summary>
    ///     The correlation ID from the request is attached to the body.
    /// </summary>
    [Fact]
    public async Task Correlation_id_is_attached_to_the_body()
    {
        var (problem, _) = await HandleAsync(
            new NotFoundException("Policy", 1), correlationId: "trace-xyz");

        // Extensions is IDictionary<string, object?> annotated for extension data, so the value's
        // static type is object even though it deserializes as a JsonElement.
        var correlationId = Assert.IsType<JsonElement>(problem.Extensions["correlationId"]);
        Assert.Equal("trace-xyz", correlationId.GetString());
    }

    /// <summary>
    ///     <c>Instance</c> records the path that failed.
    /// </summary>
    [Fact]
    public async Task Instance_records_the_request_path()
    {
        var (problem, _) = await HandleAsync(new NotFoundException("Policy", 1), path: "/api/policies/1");

        Assert.Equal("/api/policies/1", problem.Instance);
    }

    private static async Task<(ProblemDetails Problem, int Status)> HandleAsync(
        Exception exception,
        string? correlationId = null,
        string path = "/api/test")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        services.AddSingleton<GlobalExceptionHandler>();

        var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Path = path;
        if (correlationId is not null) context.Items[CorrelationIdMiddleware.ItemKey] = correlationId;

        var body = new MemoryStream();
        context.Response.Body = body;

        var handler = new GlobalExceptionHandler(
            provider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);

        body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return (problem!, context.Response.StatusCode);
    }
}
