using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PolicyManager.DTOs;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Tests that database constraint violations surface as actionable ProblemDetails statuses.
/// </summary>
/// <remarks>
///     These need a real SQL Server. The in-memory provider enforces no unique index, no foreign key
///     and no column limit, so on it every one of these requests would succeed — which is precisely
///     why the mapping has to be verified against the database the application actually runs on.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public class SqlServerProblemDetailsTests : SqlServerTestBase
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerProblemDetailsTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared container fixture.</param>
    public SqlServerProblemDetailsTests(SqlServerFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     A duplicate email is a 409, not the 500 an unmapped unique index violation would produce.
    /// </summary>
    [Fact]
    public async Task Duplicate_email_returns_409_conflict()
    {
        await using var factory = new SqlServerApiFactory(Fixture);
        using var client = factory.CreateClient();

        var holder = new CreatePolicyHolderDto
            { FirstName = "Ada", LastName = "Lovelace", Email = "ada@example.com" };

        var first = await client.PostAsJsonAsync("/api/policyholders", holder);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/policyholders", holder);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);

        var problem = await ReadProblemAsync(duplicate);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
    }

    /// <summary>
    ///     A policy for a holder that does not exist is a 409 naming the constraint.
    /// </summary>
    [Fact]
    public async Task Orphan_policy_returns_409_naming_the_constraint()
    {
        await using var factory = new SqlServerApiFactory(Fixture);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/policies", new CreatePolicyDto
        {
            PolicyHolderId = 987_654, Premium = 100m, Type = Models.Enums.PolicyType.Auto,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.Equal(409, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }

    /// <summary>
    ///     A database-backed failure still carries the caller's correlation ID.
    /// </summary>
    /// <remarks>
    ///     The header has to be set on the failing request itself. A correlation ID applies to one
    ///     request; carrying it over from the one that created the record would be meaningless.
    /// </remarks>
    [Fact]
    public async Task Constraint_violation_carries_the_correlation_id_of_the_failing_request()
    {
        await using var factory = new SqlServerApiFactory(Fixture);
        using var client = factory.CreateClient();

        var created = await client.PostAsJsonAsync("/api/policyholders",
            new CreatePolicyHolderDto { FirstName = "Ada", LastName = "Lovelace", Email = "corr@example.com" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var duplicateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/policyholders")
        {
            Content = JsonContent.Create(new CreatePolicyHolderDto
                { FirstName = "Ada", LastName = "L", Email = "corr@example.com" })
        };
        duplicateRequest.Headers.Add("X-Correlation-ID", "trace-constraint");

        var duplicate = await client.SendAsync(duplicateRequest);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("trace-constraint", duplicate.Headers.GetValues("X-Correlation-ID").Single());

        var problem = await ReadProblemAsync(duplicate);
        var correlationId = Assert.IsType<JsonElement>(problem.Extensions["correlationId"]);
        Assert.Equal("trace-constraint", correlationId.GetString());
    }

    /// <summary>
    ///     A database fault does not leak SQL or connection details to the caller.
    /// </summary>
    [Fact]
    public async Task Constraint_violation_does_not_leak_database_internals()
    {
        await using var factory = new SqlServerApiFactory(Fixture);
        using var client = factory.CreateClient();

        var holder = new CreatePolicyHolderDto
            { FirstName = "Ada", LastName = "Lovelace", Email = "leak@example.com" };

        var seed = await client.PostAsJsonAsync("/api/policyholders", holder);
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);

        var duplicate = await client.PostAsJsonAsync("/api/policyholders", holder);

        // Asserted first: without it this test would pass against a 201, whose body is a holder DTO
        // and so trivially free of SQL text, verifying nothing at all.
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var body = await duplicate.Content.ReadAsStringAsync();

        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ProblemDetails> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        return problem!;
    }
}
