using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PolicyManager.DTOs;
using PolicyManager.Middleware;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Covers the rate limiter and the request-body size limit.
/// </summary>
/// <remarks>
///     The quota is configured down to a handful of requests so the limit is reached in a test
///     rather than being asserted only in theory. Every request in a given test sends the same
///     correlation ID, which is the key the limiter counts against, so the tests do not depend on
///     how many connections the test server happens to use.
/// </remarks>
public class RateLimitingTests : ApiIntegrationTestBase
{
    private const string QuotaKey = "rate-limit-test-key";
    private const int Permits = 3;

    /// <inheritdoc />
    protected override void ConfigureTestHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RateLimiting:Enabled"] = "true",
                ["RateLimiting:PermitLimit"] = Permits.ToString(),
                ["RateLimiting:Window"] = "00:05:00",
                ["RateLimiting:Cooldown"] = "00:00:30",
                ["RateLimiting:QueueLimit"] = "0",
                ["RateLimiting:MaxRequestBodySizeBytes"] = "1024"
            }));
    }

    [Fact]
    public async Task A_caller_over_the_quota_is_told_to_retry_later()
    {
        var responses = await SendManyAsync(Permits + 1);
        var statuses = responses.Select(r => r.StatusCode).ToArray();

        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task A_rejection_advertises_how_long_to_wait()
    {
        for (var i = 0; i < Permits; i++) await SendOneAsync();

        var response = await SendOneAsync();

        // A bare 429 tells a client only that it was too fast, not for how long to slow down, so it
        // either retries immediately or gives up.
        Assert.Equal("30", response.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task A_rejection_is_a_rate_limit_answer_not_a_server_fault()
    {
        for (var i = 0; i < Permits; i++) await SendOneAsync();

        var response = await SendOneAsync();

        // Deliberately not a ProblemDetails body: 429 is a statement about the caller's pacing, not
        // a failure of this request, and the exception handler is for the latter.
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task A_different_caller_gets_its_own_quota()
    {
        await SendManyAsync(Permits, QuotaKey);

        using var second = new HttpRequestMessage(HttpMethod.Get, "/api/policyholders");
        second.Headers.Add(CorrelationIdMiddleware.HeaderName, "a-different-caller");

        var response = await Client.SendAsync(second);

        // Keying on the caller's correlation ID rather than the connection is what stops one noisy
        // client from consuming the quota of everyone behind the same address.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_body_is_refused_before_the_handler_runs()
    {
        using var content = new StringContent(new string('x', 4096), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/policyholders") { Content = content };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, QuotaKey);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_body_is_refused_with_a_traceable_problem_details()
    {
        using var content = new StringContent(new string('x', 4096), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/policyholders") { Content = content };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, QuotaKey);

        var response = await Client.SendAsync(request);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(413, body.RootElement.GetProperty("status").GetInt32());
        // Every failure has to be searchable, including this one.
        Assert.Equal(QuotaKey, body.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task A_body_within_the_limit_is_accepted()
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new CreatePolicyHolderDto
            {
                FirstName = "Small", LastName = "Body", Email = "small@test"
            }),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/policyholders") { Content = content };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, QuotaKey);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<HttpResponseMessage[]> SendManyAsync(int count, string key = QuotaKey)
    {
        var responses = new List<HttpResponseMessage>(count);

        for (var i = 0; i < count; i++) responses.Add(await SendOneAsync(key));

        return [.. responses];
    }

    private async Task<HttpResponseMessage> SendOneAsync(string key = QuotaKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/policyholders");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, key);
        return await Client.SendAsync(request);
    }
}
