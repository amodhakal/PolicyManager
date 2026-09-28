using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PolicyManager.Configuration;

namespace PolicyManager.Telemetry;

/// <summary>
///     Registers OpenTelemetry tracing and metrics for the API, its database and its outbound calls.
/// </summary>
public static class TelemetryRegistration
{
    /// <summary>
    ///     The activity source MassTransit publishes and consumes under.
    /// </summary>
    public const string MassTransitSource = "MassTransit";

    /// <summary>
    ///     Instruments the application.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Collection is always registered; export is not. An application with no collector configured
    ///     still builds spans and metrics, so turning on the OTLP exporter later is a configuration
    ///     change and not a deployment of new code. What is off unless
    ///     <see cref="TelemetryOptions.OtlpEndpoint" /> is set is anything leaving the process — which
    ///     is what keeps the default local-dev and CI paths from shipping telemetry anywhere.
    ///     </para>
    ///     <para>
    ///     Registering the services unconditionally rather than inside the same branch as the exporter is
    ///     also what makes the in-memory provider usable in tests: the pipelines and instrumentation are
    ///     identical, only the terminal exporter differs.
    ///     </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Configuration carrying the <c>Telemetry</c> section.</param>
    /// <param name="environment">The host environment, used to fill in the deployment attribute.</param>
    public static IServiceCollection AddPolicyManagerTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));

        var options = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
                      ?? new TelemetryOptions();

        options.Validate();

        void ApplyResource(ResourceBuilder r) => r
            .AddService(
                options.ServiceName,
                serviceVersion: options.ServiceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = options.Environment ?? environment.EnvironmentName
            });

        if (options.EnableTracing)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(ApplyResource)
                .WithTracing(tracer =>
                {
                    ConfigureTracing(tracer, options);

                    // The only condition on export anywhere in this method. No endpoint configured
                    // means nothing leaves the process, which is what keeps the default local-dev and
                    // CI paths from shipping telemetry anywhere.
                    if (options.Exports)
                        tracer.AddOtlpExporter(o => Otlp(o, options));
                });
        }

        if (options.EnableMetrics)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(ApplyResource)
                .WithMetrics(meter =>
                {
                    ConfigureMetrics(meter);

                    if (options.Exports)
                        meter.AddOtlpExporter(o => Otlp(o, options));
                });
        }

        // The same attributes, built once and exposed, so a diagnostic endpoint or a future exporter
        // can report how this instance identifies itself without reconstructing the configuration.
        var exposed = ResourceBuilder.CreateDefault();
        ApplyResource(exposed);
        services.AddSingleton(exposed.Build());

        return services;
    }

    private static void ConfigureTracing(TracerProviderBuilder tracing, TelemetryOptions options)
    {
        tracing
            .AddSource(MassTransitSource)
            .AddAspNetCoreInstrumentation(o => o.RecordException = true)
            .AddHttpClientInstrumentation(o => o.RecordException = true)
            // No configuration hook is offered for EF query parameters on purpose: the instrumentation
            // excludes them, and it should stay that way. A parameter can carry a policyholder's email,
            // and a trace backend is a far wider audience than the database the row lives in. The
            // statement text is recorded, which is enough to see which query is slow.
            .AddEntityFrameworkCoreInstrumentation()
            .SetSampler(new TraceIdRatioBasedSampler(options.TraceSampleRatio));
    }

    private static void ConfigureMetrics(MeterProviderBuilder metrics)
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(MassTransitSource);
    }

    private static void Otlp(OtlpExporterOptions o, TelemetryOptions options)
    {
        o.Endpoint = new Uri(options.OtlpEndpoint!);

        if (string.Equals(options.OtlpProtocol, "http/protobuf", StringComparison.OrdinalIgnoreCase))
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
    }
}
