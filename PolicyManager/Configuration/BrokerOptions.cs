namespace PolicyManager.Configuration;

/// <summary>
///     Connection and topology settings for the message broker that receives the transactional outbox.
/// </summary>
/// <remarks>
///     <para>
///     Bound from the <c>Broker</c> configuration section. <see cref="Enabled" /> is the switch that
///     decides which <c>IOutboxPublisher</c> is registered, and it is <see langword="false" /> by
///     default: with no broker configured the application still starts, still dispatches its outbox
///     and still tests green, because <c>LoggingOutboxPublisher</c> stands in for the transport.
///     </para>
///     <para>
///     That is a deliberate default rather than a convenience one. Making the transport mandatory
///     would mean every contributor needed a running broker before the test suite would even start,
///     and CI would grow a service dependency for a path that is not what the tests are about.
///     </para>
/// </remarks>
public class BrokerOptions
{
    /// <summary>
    ///     The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "Broker";

    /// <summary>
    ///     Whether to publish the outbox to a real broker.
    /// </summary>
    /// <remarks>
    ///     <see langword="false" /> registers the logging stand-in; <see langword="true" /> registers
    ///     the MassTransit publisher and starts a RabbitMQ bus. Set with <c>Broker__Enabled</c> or
    ///     <c>Broker:Enabled</c>.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    ///     Broker hostname.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    ///     Broker port. 5672 is AMQP; 5671 is AMQP over TLS.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    ///     Virtual host the bus connects to.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    ///     Username used to connect.
    /// </summary>
    public string Username { get; set; } = "guest";

    /// <summary>
    ///     Password used to connect.
    /// </summary>
    /// <remarks>
    ///     Read from configuration so it can be supplied by a secret store or an environment
    ///     variable rather than committed. The default matches the RabbitMQ image's development
    ///     account and is not a credential worth protecting.
    /// </remarks>
    public string Password { get; set; } = "guest";

    /// <summary>
    ///     Whether to negotiate TLS with the broker.
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    ///     The durable queue outbox messages are published to.
    /// </summary>
    /// <remarks>
    ///     Declared and bound to the publish exchange at bus start, so the queue exists before the
    ///     first message is sent rather than being created implicitly by whoever is consuming.
    /// </remarks>
    public string QueueName { get; set; } = "policy-manager.outbox";

    /// <summary>
    ///     Whether the queue and the exchange survive a broker restart.
    /// </summary>
    /// <remarks>
    ///     <see langword="true" /> is the only correct setting for a queue an outbox publishes into:
    ///     a non-durable queue is discarded when the broker restarts, which would lose exactly the
    ///     messages the outbox exists to guarantee.
    /// </remarks>
    public bool Durable { get; set; } = true;

    /// <summary>
    ///     AMQP heartbeat interval. Detects a silently dropped connection instead of waiting for a
    ///     publish to time out.
    /// </summary>
    public TimeSpan RequestedHeartbeat { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long to wait for the initial broker connection before giving up.
    /// </summary>
    public TimeSpan RequestedConnectionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How long a single publish may take before it is abandoned.
    /// </summary>
    /// <remarks>
    ///     Bounded so a publish cannot hold a claimed outbox message past the point where its lease
    ///     expires and a second processor picks it up.
    /// </remarks>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Throws when the broker is enabled but not usable as configured.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required setting is missing or out of range.</exception>
    public void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Host)) errors.Add($"{SectionName}:{nameof(Host)} must be set.");
        if (string.IsNullOrWhiteSpace(Username)) errors.Add($"{SectionName}:{nameof(Username)} must be set.");
        if (string.IsNullOrWhiteSpace(Password)) errors.Add($"{SectionName}:{nameof(Password)} must be set.");
        if (string.IsNullOrWhiteSpace(QueueName)) errors.Add($"{SectionName}:{nameof(QueueName)} must be set.");

        if (Port is < 1 or > 65535)
            errors.Add($"{SectionName}:{nameof(Port)} must be between 1 and 65535 but was {Port}.");

        if (RequestedHeartbeat <= TimeSpan.Zero)
            errors.Add($"{SectionName}:{nameof(RequestedHeartbeat)} must be positive.");

        if (RequestedConnectionTimeout <= TimeSpan.Zero)
            errors.Add($"{SectionName}:{nameof(RequestedConnectionTimeout)} must be positive.");

        if (PublishTimeout <= TimeSpan.Zero)
            errors.Add($"{SectionName}:{nameof(PublishTimeout)} must be positive.");

        if (errors.Count == 0) return;

        throw new InvalidOperationException(
            $"The message broker is enabled but not configured correctly: {string.Join(" ", errors)}");
    }
}
