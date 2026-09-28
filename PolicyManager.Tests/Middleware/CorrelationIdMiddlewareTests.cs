using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PolicyManager.Middleware;

namespace PolicyManager.Tests.Middleware;

/// <summary>
///     Tests for the identifier <see cref="CorrelationIdMiddleware" /> chooses for a request.
/// </summary>
/// <remarks>
///     Asserted against the middleware directly rather than through <see cref="HttpClient" />. The
///     inbound header is attacker-controlled, and the cases worth pinning are precisely the ones a
///     client cannot be made to send: <see cref="HttpHeaders" /> rejects a value containing CR or LF
///     before the request leaves the test, so an integration test that sends one only proves that
///     <c>HttpClient</c> validates header values, not that the middleware sanitises them.
/// </remarks>
public class CorrelationIdMiddlewareTests
{
    /// <summary>
    ///     A usable inbound identifier is reused, so a trace started upstream survives the hop.
    /// </summary>
    [Fact]
    public async Task Usable_inbound_identifier_is_reused()
    {
        var (assigned, _) = await InvokeAsync(context => context.Request.Headers[CorrelationIdMiddleware.HeaderName]
            = "trace-from-the-edge");

        Assert.Equal("trace-from-the-edge", assigned);
    }

    /// <summary>
    ///     An identifier carrying a header-injection attempt is discarded in favour of the
    ///     connection's own trace identifier.
    /// </summary>
    /// <remarks>
    ///     Nothing sanitises the value before it is echoed back and written into the logging scope, so
    ///     a value carrying CR or LF is a response-splitting and log-forging vector. It must never be
    ///     adopted, and must not be trimmed into something that looks legitimate either.
    /// </remarks>
    [Theory]
    [InlineData("trace\r\nInjected: yes")]
    [InlineData("trace\r\nX-Evil: yes\r\n\r\nbody")]
    [InlineData("trace\twith\ttabs")]
    [InlineData("trace with spaces")]
    [InlineData("")]
    public async Task Unusable_inbound_identifier_is_discarded(string inbound)
    {
        var (assigned, fallback) = await InvokeAsync(context => context.Request.Headers[CorrelationIdMiddleware.HeaderName]
            = inbound);

        Assert.Equal(fallback, assigned);
    }

    /// <summary>
    ///     An identifier longer than the cap is discarded rather than truncated.
    /// </summary>
    [Fact]
    public async Task Over_long_inbound_identifier_is_discarded()
    {
        var absurd = new string('x', 5000);

        var (assigned, fallback) = await InvokeAsync(context => context.Request.Headers[CorrelationIdMiddleware.HeaderName]
            = absurd);

        Assert.Equal(fallback, assigned);
    }

    /// <summary>
    ///     Runs the middleware over a request carrying the supplied inbound header, and returns the
    ///     identifier it put in scope alongside the one it had to fall back on.
    /// </summary>
    /// <param name="setHeader">Sets the inbound header on the request under test.</param>
    /// <returns>The resolved correlation identifier and the connection's own trace identifier.</returns>
    private static async Task<(string? Assigned, string Fallback)> InvokeAsync(Action<DefaultHttpContext> setHeader)
    {
        var context = new DefaultHttpContext();
        setHeader(context);

        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        return (context.Items[CorrelationIdMiddleware.ItemKey] as string, context.TraceIdentifier);
    }
}
