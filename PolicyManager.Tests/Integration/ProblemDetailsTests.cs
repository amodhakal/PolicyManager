using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PolicyManager.DTOs;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Tests that failures leave the API as ProblemDetails carrying a correlation ID.
/// </summary>
/// <remarks>
///     Correlation-ID propagation and status rendering are pipeline behaviour, so they are asserted
///     through the real HTTP pipeline on the in-memory provider. The mapping from an exception to a
///     status lives in <c>GlobalExceptionHandler</c> and is covered by unit tests against that class;
///     database-constraint cases need SQL Server and live in <c>SqlServerApiTests</c>.
/// </remarks>
public class ProblemDetailsTests : ApiIntegrationTestBase
{
    /// <summary>
    ///     Every response carries a correlation ID header.
    /// </summary>
    [Fact]
    public async Task Successful_response_carries_a_correlation_id()
    {
        var response = await Client.GetAsync("/api/policyholders");

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("X-Correlation-ID").First()));
    }

    /// <summary>
    ///     A correlation ID supplied by the caller is preserved rather than replaced.
    /// </summary>
    /// <remarks>
    ///     This is what lets a trace started upstream survive the hop into this service.
    /// </remarks>
    [Fact]
    public async Task Inbound_correlation_id_is_preserved()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/policyholders");
        request.Headers.Add("X-Correlation-ID", "trace-from-the-edge");

        var response = await Client.SendAsync(request);

        Assert.Equal("trace-from-the-edge", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    /// <summary>
    ///     A 404 is rendered as ProblemDetails rather than an empty body.
    /// </summary>
    [Fact]
    public async Task Not_found_is_returned_as_problem_details()
    {
        var response = await Client.GetAsync("/api/policyholders/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Instance));
    }

    /// <summary>
    ///     The ProblemDetails body carries the same correlation ID as the response header.
    /// </summary>
    /// <remarks>
    ///     This is the whole point of the identifier: a user quotes it, and support can join it to the
    ///     log lines for that one request.
    /// </remarks>
    [Fact]
    public async Task Problem_details_body_includes_the_correlation_id()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/policyholders/999999");
        request.Headers.Add("X-Correlation-ID", "trace-abc-123");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);

        // Extensions is IDictionary<string, object?> annotated for extension data, so the value's
        // static type is object even though it deserializes as a JsonElement.
        var correlationId = Assert.IsType<JsonElement>(problem!.Extensions["correlationId"]);
        Assert.Equal("trace-abc-123", correlationId.GetString());
        Assert.Equal("trace-abc-123", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    /// <summary>
    ///     A model-binding failure is a 400 rendered as ProblemDetails, naming the failing fields.
    /// </summary>
    /// <remarks>
    ///     Driven through <see cref="CreatePolicyDto" />, which carries a <c>[Range]</c> on
    ///     <c>Premium</c> and an <c>IValidatableObject</c> date check. A zero premium is below the
    ///     storable range, so binding fails before the action runs.
    /// </remarks>
    [Fact]
    public async Task Validation_failure_is_returned_as_validation_problem_details()
    {
        var response = await Client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = 1,
            Premium = 0m,
            Type = Models.Enums.PolicyType.Auto,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2027, 1, 1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("errors", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     A created resource reports a correlation ID too, not only failures.
    /// </summary>
    [Fact]
    public async Task Created_response_carries_a_correlation_id()
    {
        var response = await Client.PostAsJsonAsync("/api/policyholders",
            new CreatePolicyHolderDto { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("X-Correlation-ID").First()));
    }

    /// <summary>
    ///     The correlation ID header survives the exception path, which clears the response headers.
    /// </summary>
    /// <remarks>
    ///     The exception handler calls <c>Response.Clear()</c> before writing its body, and that
    ///     wipes the header collection. Setting the header eagerly therefore loses it on exactly the
    ///     responses that matter, so the middleware registers it through <c>OnStarting</c> instead.
    ///     Asserted explicitly because the body still carries the ID either way, which masks it.
    /// </remarks>
    [Fact]
    public async Task Correlation_id_header_survives_the_exception_path()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/policyholders/999999");
        request.Headers.Add("X-Correlation-ID", "trace-cleared-headers");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("trace-cleared-headers", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    /// <summary>
    ///     An over-long inbound correlation ID is discarded rather than echoed back.
    /// </summary>
    /// <remarks>
    ///     The value is attacker-controlled and reaches the logging scope for every line of the
    ///     request, so it is length-capped rather than trusted. The response must still carry a
    ///     usable identifier, just not the one that was sent.
    /// </remarks>
    [Fact]
    public async Task Over_long_inbound_correlation_id_is_replaced()
    {
        var absurd = new string('x', 5000);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/policyholders");
        request.Headers.Add("X-Correlation-ID", absurd);

        var response = await Client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var assigned = response.Headers.GetValues("X-Correlation-ID").Single();

        Assert.NotEqual(absurd, assigned);
        Assert.True(assigned.Length is > 0 and <= 128);
    }
}
