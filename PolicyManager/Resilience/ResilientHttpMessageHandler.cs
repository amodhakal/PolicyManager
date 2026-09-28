using Microsoft.Extensions.Options;
using Polly;
using PolicyManager.Configuration;

namespace PolicyManager.Resilience;

/// <summary>
///     Applies the HTTP resilience pipeline to every outbound request made through the client it is
///     attached to.
/// </summary>
/// <remarks>
///     <para>
///     A <see cref="DelegatingHandler" /> rather than a call at each call site, so a new outbound call
///     is protected the moment it is written. A policy applied by hand at each call site is a policy
///     the next contributor forgets.
///     </para>
///     <para>
///     It is attached to named clients only, never to <see cref="System.Net.Http.HttpClient" /> as a
///     type: the pipeline is built once and shared, so there is a single set of counters and a single
///     circuit state per dependency rather than one per handler instance.
///     </para>
/// </remarks>
public sealed class ResilientHttpMessageHandler(
    ResiliencePipelineFor<HttpPipeline> pipelines,
    IOptions<ResilienceOptions> options) : DelegatingHandler
{
    private readonly DependencyResilienceOptions _options = options.Value.Http;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await pipelines.Pipeline.ExecuteAsync(
            async token => await base.SendAsync(request, token),
            cancellationToken);
    }

    /// <summary>
    ///     The budget this handler enforces, exposed for the tests that assert on it.
    /// </summary>
    public DependencyResilienceOptions Options => _options;
}
