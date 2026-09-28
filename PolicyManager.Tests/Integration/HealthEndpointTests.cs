using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PolicyManager.Health;
using PolicyManager.Resilience;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Smoke tests for the health endpoints, which now run their probe through the database resilience
///     pipeline.
/// </summary>
/// <remarks>
///     Exists because the readiness probe was re-registered — as a
///     <see cref="HealthCheckRegistration" /> rather than by type — so that the decorating wrapper and
///     the probe it wraps could both be built from the container. A registration that does not resolve
///     fails at probe time rather than at startup, and a probe nobody ever calls is exactly the thing
///     that goes unnoticed.
/// </remarks>
public class HealthEndpointTests : IAsyncLifetime
{
    private readonly InMemoryApiFactory _factory = new();

    private HttpClient Client { get; set; } = null!;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        // The health endpoints are unauthenticated, which is why this test can use a bare client.
        Client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>
    ///     The liveness endpoint answers, and it reports the database probe by name. The probe fails
    ///     against the in-memory provider — it issues SQL, which that provider does not implement — so
    ///     what is being asserted is that the check is present and was run, not that it passed. The
    ///     503 is the endpoint's own default for an unhealthy report and predates the wrapper.
    /// </summary>
    [Fact]
    public async Task Liveness_reports_the_database_probe()
    {
        var response = await Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var checks = body.RootElement.GetProperty("checks");

        Assert.True(checks.TryGetProperty("sql-server", out var probe));
        Assert.Equal("Unhealthy", probe.GetProperty("status").GetString());
        Assert.True(probe.GetProperty("durationMs").GetDouble() >= 0);
    }

    /// <summary>
    ///     The readiness endpoint runs only the checks tagged ready, and the database probe is the only
    ///     one registered under that tag.
    /// </summary>
    [Fact]
    public async Task Readiness_runs_the_database_probe_and_nothing_else()
    {
        var response = await Client.GetAsync("/health/ready");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("Unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Single(body.RootElement.GetProperty("checks").EnumerateObject());
    }

    /// <summary>
    ///     The decorated probe resolves from the built host, and a failure short of an exception is
    ///     reported as unhealthy with a redacted description — the reason never reaches an
    ///     unauthenticated caller.
    /// </summary>
    [Fact]
    public async Task The_wrapped_probe_resolves_from_the_built_host()
    {
        using var scope = _factory.Services.CreateScope();

        var services = scope.ServiceProvider;
        var wrapper = new ResilientSqlServerHealthCheck(
            ActivatorUtilities.CreateInstance<SqlServerHealthCheck>(services),
            services.GetRequiredService<ResiliencePipelineFor<DatabasePipeline>>(),
            services.GetRequiredService<ILogger<ResilientSqlServerHealthCheck>>());

        var result = await wrapper.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(SqlServerHealthCheck.UnhealthyDescription, result.Description);
    }
}
