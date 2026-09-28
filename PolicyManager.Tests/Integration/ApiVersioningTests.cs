using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using PolicyManager.DTOs;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Covers the versioning contract, and above all that introducing it changed nothing for a client
///     that does not ask for a version.
/// </summary>
public class ApiVersioningTests : ApiIntegrationTestBase
{
    [Theory]
    [InlineData("/api/policyholders")]
    [InlineData("/api/v1.0/policyholders")]
    public async Task Both_the_unversioned_and_the_versioned_route_reach_the_same_endpoint(string path)
    {
        var response = await Client.GetAsync(path);

        // The unversioned route is the one every existing client already calls. It is kept, not
        // replaced, so introducing a version was not a breaking change.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/policyholders")]
    [InlineData("/api/v1.0/policyholders")]
    [InlineData("/api/claims/1")]
    [InlineData("/api/policies/1")]
    public async Task An_endpoint_advertises_the_versions_it_supports(string path)
    {
        var response = await Client.GetAsync(path);

        // ReportApiVersions puts this in a header, so a client can discover the supported versions
        // from a single call rather than from documentation it has to trust to be current.
        var supported = response.Headers.GetValues("api-supported-versions").ToArray();
        Assert.Equal(["1.0"], supported);
    }

    [Fact]
    public async Task An_unknown_version_is_rejected()
    {
        var response = await Client.GetAsync("/api/v2.0/policyholders");

        // 404 here but 400 for ?api-version=2.0, and the difference is worth stating. The version
        // segment is part of the route, so a version nothing declares means no route matches and the
        // request is indistinguishable from a path that does not exist. The query string is read
        // after a route has matched, so the resource was found and the only fault is the parameter.
        // Neither is a server fault, and both keep the caller in the "fix your request" bucket.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_malformed_version_is_rejected()
    {
        var response = await Client.GetAsync("/api/vbanana/policyholders");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_version_can_also_be_asked_for_in_the_query_string()
    {
        var response = await Client.GetAsync("/api/policyholders?api-version=1.0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unsupported_version_in_the_query_string_is_rejected()
    {
        var response = await Client.GetAsync("/api/policyholders?api-version=2.0");

        // Silently serving 1.0 here would be the worst outcome available: the caller asked for a
        // contract it is not being given and would have no way to tell.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_write_through_the_versioned_route_behaves_identically()
    {
        var created = await Client.PostAsJsonAsync("/api/v1.0/policyholders",
            new CreatePolicyHolderDto { FirstName = "Version", LastName = "One", Email = "v1@test" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Read back through the unversioned route: the two routes are one endpoint, not two that
        // have to be kept in step.
        var body = await created.Content.ReadFromJsonAsync<PolicyHolderDto>();
        var readBack = await Client.GetFromJsonAsync<PolicyHolderDto>(
            $"/api/policyholders/{body!.Id}");

        Assert.Equal("v1@test", readBack!.Email);
    }

    [Fact]
    public async Task The_problem_details_response_is_reachable_through_the_versioned_route()
    {
        var response = await Client.GetAsync("/api/v1.0/policies/999999");

        // The version constraint runs before routing completes, so this proves a versioned URL still
        // reaches the same exception handler and gets the same traceable body.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Every_controller_declares_a_version()
    {
        // A controller that forgets [ApiVersion] is not a compile error; it is a set of routes that
        // silently stop being reachable through the versioned template. Asserted so it cannot ship.
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.Namespace?.StartsWith("PolicyManager.Controllers", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            Assert.True(
                controller.GetCustomAttributes(typeof(ApiVersionAttribute), inherit: true).Length > 0,
                $"{controller.Name} has no [ApiVersion], so its routes would not be reachable at a " +
                "versioned URL.");
        }
    }
}
