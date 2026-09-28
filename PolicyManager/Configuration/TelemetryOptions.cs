namespace PolicyManager.Configuration;

/// <summary>
///     Controls what the application emits through OpenTelemetry, and where it goes.
/// </summary>
/// <remarks>
///     <para>
///     Bound from the <c>Telemetry</c> configuration section.
///     </para>
///     <para>
///     Collection and export are separate switches on purpose. An application with no exporter
///     configured still records spans and metrics, which costs a bounded amount of memory and CPU
///     and is what makes the OTLP exporter a configuration change rather than a code change. What is
///     off by default is the export: nothing leaves the process unless a collector endpoint is named,
///     so a developer running the suite locally — and CI — is not quietly shipping telemetry to
///     somewhere.
///     </para>
/// </remarks>
public class TelemetryOptions
{
    /// <summary>
    ///     The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "Telemetry";

    /// <summary>
    ///     The <c>service.name</c> resource attribute every span and metric is attributed to.
    /// </summary>
    public string ServiceName { get; set; } = "policy-manager-api";

    /// <summary>
    ///     The <c>service.version</c> resource attribute.
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    ///     The <c>deployment.environment</c> resource attribute, defaulted from the host
    ///     environment when not set.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    ///     Whether distributed tracing is collected at all.
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    ///     Whether metrics are collected at all.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    ///     Whether a collector endpoint has been configured, and therefore whether anything is
    ///     exported at all.
    /// </summary>
    /// <remarks>
    ///     The one condition on export in the whole application, exposed so it can be asserted on
    ///     directly rather than inferred from the SDK's internals.
    /// </remarks>
    public bool Exports => !string.IsNullOrWhiteSpace(OtlpEndpoint);

    /// <summary>
    ///     The OTLP/gRPC collector endpoint, for example <c>http://localhost:4317</c>.
    /// </summary>
    /// <remarks>
    ///     Null — the default — means no OTLP exporter is registered. Setting it is what turns export
    ///     on; nothing is exported merely because tracing is enabled.
    /// </remarks>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    ///     Which OTLP transport to use: <c>grpc</c> or <c>http/protobuf</c>.
    /// </summary>
    public string OtlpProtocol { get; set; } = "grpc";

    /// <summary>
    ///     The ratio of traces recorded, from 0 to 1.
    /// </summary>
    /// <remarks>
    ///     Head sampling, applied to the root of a trace. Not a rate limiter: the decision is made once,
    ///     at the start, and is then carried on the trace itself so a downstream service does not
    ///     decide differently and produce a trace with a hole in the middle.
    /// </remarks>
    public double TraceSampleRatio { get; set; } = 1.0;

    /// <summary>
    ///     Throws when the configuration could not be honoured as written.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is out of range or unusable.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceName))
            throw new InvalidOperationException($"{SectionName}:{nameof(ServiceName)} must be set.");

        if (TraceSampleRatio is < 0 or > 1)
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(TraceSampleRatio)} must be between 0 and 1 but was {TraceSampleRatio}.");

        if (OtlpEndpoint is not null &&
            !string.Equals(OtlpProtocol, "grpc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(OtlpProtocol, "http/protobuf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(OtlpProtocol)} must be 'grpc' or 'http/protobuf' but was '{OtlpProtocol}'.");
    }
}
