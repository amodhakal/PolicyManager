using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Messaging;
using PolicyManager.Resilience;
using PolicyManager.Services;

namespace PolicyManager.Tests.Messaging;

/// <summary>
///     Tests for the broker configuration and for which <c>IOutboxPublisher</c> the composition root
///     actually ends up with.
/// </summary>
/// <remarks>
///     No broker is started here, and that is the point. The transport is opt-in precisely so the
///     wiring can be verified without one; a container-backed test would prove the broker is
///     reachable, which is not what could silently regress here. What could regress is the choice of
///     publisher, and that a misconfigured broker fails loudly at startup instead of quietly
///     discarding events.
/// </remarks>
public class BrokerRegistrationTests
{
    /// <summary>
    ///     A broker is off unless something turns it on, so the default build, test and local-dev
    ///     path never needs a broker container.
    /// </summary>
    [Fact]
    public async Task Broker_is_disabled_by_default()
    {
        await using var provider = Build(new Dictionary<string, string?>());

        Assert.False(provider.GetRequiredService<IOptions<BrokerOptions>>().Value.Enabled);
    }

    /// <summary>
    ///     With no broker configured, the outbox still drains — through the logging stand-in.
    /// </summary>
    [Fact]
    public async Task Logging_publisher_is_registered_when_the_broker_is_disabled()
    {
        await using var provider = Build(new Dictionary<string, string?>());

        Assert.IsType<LoggingOutboxPublisher>(provider.GetRequiredService<IOutboxPublisher>());
    }

    /// <summary>
    ///     A disabled broker starts no bus at all. If it did, the test suite would need a broker just
    ///     to build a service provider.
    /// </summary>
    [Fact]
    public async Task No_bus_is_registered_when_the_broker_is_disabled()
    {
        await using var provider = Build(new Dictionary<string, string?>());

        Assert.Null(provider.GetService<MassTransit.IPublishEndpoint>());
    }

    /// <summary>
    ///     Turning the broker on replaces the stand-in, rather than leaving two registrations for the
    ///     dispatcher to choose between.
    /// </summary>
    [Fact]
    public async Task Broker_publisher_replaces_the_stand_in_when_enabled()
    {
        await using var provider = Build(EnabledSettings());

        using var scope = provider.CreateScope();

        Assert.IsType<MassTransitOutboxPublisher>(scope.ServiceProvider.GetRequiredService<IOutboxPublisher>());
    }

    /// <summary>
    ///     Enabling the broker registers the bus the publisher publishes through.
    /// </summary>
    [Fact]
    public async Task Publish_endpoint_is_registered_when_the_broker_is_enabled()
    {
        await using var provider = Build(EnabledSettings());

        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<MassTransit.IPublishEndpoint>());
    }

    /// <summary>
    ///     Configuration is bound even when the broker is off, so <see cref="BrokerOptions" /> is
    ///     injectable either way and the settings are inspectable without a broker running.
    /// </summary>
    [Fact]
    public async Task Options_are_bound_even_when_the_broker_is_disabled()
    {
        await using var provider = Build(new Dictionary<string, string?>
        {
            ["Broker:QueueName"] = "custom.queue"
        });

        var options = provider.GetRequiredService<IOptions<BrokerOptions>>().Value;

        Assert.Equal("custom.queue", options.QueueName);
        Assert.False(options.Enabled);
    }

    /// <summary>
    ///     A broker enabled without a host is a deployment mistake, and it stops the process at
    ///     startup rather than at the first message.
    /// </summary>
    [Fact]
    public void Enabled_broker_without_a_host_fails_at_registration()
    {
        var settings = EnabledSettings();
        settings["Broker:Host"] = string.Empty;

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Broker:Host", ex.Message);
    }

    /// <summary>
    ///     A broker enabled without a queue is equally unusable, and says so.
    /// </summary>
    [Fact]
    public void Enabled_broker_without_a_queue_fails_at_registration()
    {
        var settings = EnabledSettings();
        settings["Broker:QueueName"] = "  ";

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Broker:QueueName", ex.Message);
    }

    /// <summary>
    ///     A port outside the legal range is rejected rather than passed to the AMQP client, which
    ///     would fail later and far less clearly.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Enabled_broker_with_an_invalid_port_fails_at_registration(int port)
    {
        var settings = EnabledSettings();
        settings["Broker:Port"] = port.ToString();

        var ex = Assert.Throws<InvalidOperationException>(() => Build(settings));

        Assert.Contains("Broker:Port", ex.Message);
    }

    /// <summary>
    ///     A broker with a publish budget of zero would cancel every publish before it starts, so it
    ///     is a configuration error rather than a silently useless one.
    /// </summary>
    [Fact]
    public void Enabled_broker_with_a_zero_publish_timeout_fails_at_registration()
    {
        var options = new BrokerOptions { Enabled = true, PublishTimeout = TimeSpan.Zero };

        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("Broker:PublishTimeout", ex.Message);
    }

    /// <summary>
    ///     A fully specified broker is accepted.
    /// </summary>
    [Fact]
    public void Enabled_broker_with_a_complete_configuration_is_accepted()
    {
        var options = new BrokerOptions
        {
            Enabled = true,
            Host = "rabbitmq",
            Port = 5672,
            VirtualHost = "/policy",
            Username = "policy",
            Password = "secret",
            QueueName = "policy-manager.outbox"
        };

        options.Validate();
    }

    /// <summary>
    ///     Defaults target a local broker over plain AMQP, and the queue is durable because a
    ///     transient queue would lose exactly the messages the outbox exists to guarantee.
    /// </summary>
    [Fact]
    public void Defaults_target_a_local_broker_over_plain_amqp()
    {
        var options = new BrokerOptions();

        Assert.False(options.Enabled);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(5672, options.Port);
        Assert.Equal("/", options.VirtualHost);
        Assert.Equal("guest", options.Username);
        Assert.False(options.UseSsl);
        Assert.True(options.Durable);
        Assert.Equal("policy-manager.outbox", options.QueueName);
    }

    private static Dictionary<string, string?> EnabledSettings() => new()
    {
        ["Broker:Enabled"] = "true",
        ["Broker:Host"] = "localhost",
        ["Broker:Username"] = "guest",
        ["Broker:Password"] = "guest",
        ["Broker:QueueName"] = "policy-manager.outbox"
    };

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(TimeProvider.System);

        // Mirrors Program.cs: the stand-in is registered first with TryAdd, so the real publisher
        // only wins because it is registered after it.
        services.TryAddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();

        // The real publisher is constructed inside the resilience pipeline, so a container that
        // mirrors Program.cs has to have the pipelines in it too.
        services.AddResiliencePipelines(new ConfigurationBuilder().Build());

        services.AddOutboxBroker(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services.BuildServiceProvider();
    }
}
