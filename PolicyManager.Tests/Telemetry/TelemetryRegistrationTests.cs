using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PolicyManager.Configuration;
using PolicyManager.Telemetry;

namespace PolicyManager.Tests.Telemetry;

/// <summary>
///     Tests for how tracing and metrics are configured, and above all for what is exported by default.
/// </summary>
/// <remarks>
///     The negative case is the one worth testing. It is easy to ship an application that exports
///     telemetry to wherever its ambient environment points, and easy to do so by accident: a default
///     that quietly resolves a collector endpoint means a developer's machine and a CI runner ship data
///     with nobody having decided to. The default has to be that nothing leaves the process.
/// </remarks>
public class TelemetryRegistrationTests
{
    /// <summary>
    ///     Collection is on by default even though export is off, so turning on a collector later is a
    ///     configuration change rather than a redeploy of new code.
    /// </summary>
    [Fact]
    public void Providers_are_registered_without_a_collector()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetService<TracerProvider>());
        Assert.NotNull(provider.GetService<MeterProvider>());
    }

    /// <summary>
    ///     No endpoint means no export. This is the assertion that keeps local development and CI from
    ///     shipping telemetry anywhere.
    /// </summary>
    [Fact]
    public void Nothing_is_exported_without_an_endpoint()
    {
        using var provider = Build();

        Assert.False(provider.GetRequiredService<IOptions<TelemetryOptions>>().Value.Exports);
    }

    /// <summary>
    ///     Naming an endpoint is what turns export on, and nothing else does.
    /// </summary>
    [Fact]
    public void Naming_an_endpoint_turns_export_on()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = "http://localhost:4317"
        });

        var options = provider.GetRequiredService<IOptions<TelemetryOptions>>().Value;

        Assert.True(options.Exports);
        Assert.Equal("http://localhost:4317", options.OtlpEndpoint);
    }

    /// <summary>
    ///     A whitespace-only endpoint is not an endpoint, and treating it as one would send spans to a
    ///     collector the operator never named.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_endpoint_is_not_an_endpoint(string endpoint)
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Telemetry:OtlpEndpoint"] = endpoint
        });

        Assert.False(provider.GetRequiredService<IOptions<TelemetryOptions>>().Value.Exports);
    }

    /// <summary>
    ///     The service name is what identifies these spans in a backend shared with other services, so
    ///     it comes from configuration and is applied to the resource.
    /// </summary>
    [Fact]
    public void The_service_name_is_applied_as_a_resource_attribute()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Telemetry:ServiceName"] = "policy-manager-canary",
            ["Telemetry:ServiceVersion"] = "9.9.9"
        });

        var attributes = provider.GetRequiredService<Resource>().Attributes.ToList();

        Assert.Contains(attributes, a => a.Key == "service.name" && (string)a.Value == "policy-manager-canary");
        Assert.Contains(attributes, a => a.Key == "service.version" && (string)a.Value == "9.9.9");
    }

    /// <summary>
    ///     The deployment environment comes from the host unless configuration says otherwise, so a
    ///     staging instance is distinguishable from production in the same backend.
    /// </summary>
    [Fact]
    public void The_deployment_environment_falls_back_to_the_host()
    {
        using var provider = Build();

        var attributes = provider.GetRequiredService<Resource>().Attributes.ToList();

        Assert.Contains(attributes, a => a.Key == "deployment.environment" && (string)a.Value == "Staging");
    }

    /// <summary>
    ///     Defaults identify the service and sample everything, so enabling an exporter does not
    ///     silently drop most of the traces.
    /// </summary>
    [Fact]
    public void Defaults_identify_the_service_and_sample_everything()
    {
        var options = new TelemetryOptions();

        Assert.Equal("policy-manager-api", options.ServiceName);
        Assert.True(options.EnableTracing);
        Assert.True(options.EnableMetrics);
        Assert.Equal(1.0, options.TraceSampleRatio);
        Assert.Null(options.OtlpEndpoint);
        Assert.False(options.Exports);
    }

    /// <summary>
    ///     Tracing and metrics can each be switched off independently, so a deployment that only wants
    ///     metrics does not pay for tracing.
    /// </summary>
    [Fact]
    public void Tracing_and_metrics_can_be_disabled_independently()
    {
        using var withoutTracing = Build(new Dictionary<string, string?>
        {
            ["Telemetry:EnableTracing"] = "false"
        });

        using var withoutMetrics = Build(new Dictionary<string, string?>
        {
            ["Telemetry:EnableMetrics"] = "false"
        });

        Assert.Null(withoutTracing.GetService<TracerProvider>());
        Assert.NotNull(withoutTracing.GetService<MeterProvider>());

        Assert.NotNull(withoutMetrics.GetService<TracerProvider>());
        Assert.Null(withoutMetrics.GetService<MeterProvider>());
    }

    /// <summary>
    ///     A sample ratio outside 0..1 is rejected, because a sampler silently clamped to a range would
    ///     quietly change how much telemetry a deployment produces.
    /// </summary>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void An_out_of_range_sample_ratio_is_rejected(double ratio)
    {
        var options = new TelemetryOptions { TraceSampleRatio = ratio };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Telemetry:TraceSampleRatio", ex.Message);
    }

    /// <summary>
    ///     An unrecognised OTLP transport is rejected at startup rather than falling back to gRPC, which
    ///     would send spans somewhere the operator did not ask for.
    /// </summary>
    [Fact]
    public void An_unrecognised_otlp_protocol_is_rejected()
    {
        var options = new TelemetryOptions
        {
            OtlpEndpoint = "http://localhost:4317",
            OtlpProtocol = "carrier-pigeon"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Telemetry:OtlpProtocol", ex.Message);
    }

    /// <summary>
    ///     An empty service name would produce spans nothing can be attributed to, so it is rejected.
    /// </summary>
    [Fact]
    public void An_empty_service_name_is_rejected()
    {
        var options = new TelemetryOptions { ServiceName = "  " };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Telemetry:ServiceName", ex.Message);
    }

    private static ServiceProvider Build(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();

        var environment = new StubEnvironment { EnvironmentName = "Staging" };

        services.AddPolicyManagerTelemetry(
            new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build(),
            environment);

        return services.BuildServiceProvider();
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "PolicyManager.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
