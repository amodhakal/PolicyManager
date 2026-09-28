using MassTransit;
using MassTransit.DependencyInjection;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PolicyManager.Configuration;
using PolicyManager.Messaging;
using PolicyManager.Models;
using PolicyManager.Resilience;
using PolicyManager.Services;

namespace PolicyManager.Tests.Messaging;

/// <summary>
///     Tests for the publisher that hands the outbox to a real broker.
/// </summary>
/// <remarks>
///     The publish path is run against MassTransit's in-memory harness rather than a broker, which is
///     the second half of the point of making the transport opt-in: the message really goes through
///     MassTransit's pipeline, with its headers and its identifier, and there is still nothing to
///     start first. What is asserted is the contract the dispatcher depends on — a success is
///     reported as a success, and a transport failure is allowed to escape so the existing retry and
///     dead-letter policy sees it.
/// </remarks>
public class MassTransitOutboxPublisherTests
{
    /// <summary>
    ///     The outbox row's identity, discriminator, payload and creation time all reach the broker
    ///     unchanged. A consumer correlating what it received with what the outbox recorded depends on
    ///     every one of them.
    /// </summary>
    [Fact]
    public async Task Publishes_the_outbox_row_as_an_envelope()
    {
        await using var harness = await Harness.StartAsync();

        var message = NewMessage();
        await harness.Publisher.PublishAsync(message);

        var envelope = Assert.Single(harness.PublishedEnvelopes()).Context.Message;

        Assert.Equal(message.Id, envelope.MessageId);
        Assert.Equal(message.Type, envelope.Type);
        Assert.Equal(message.Content, envelope.Content);
        Assert.Equal(message.CreatedAt, envelope.CreatedAt);
        Assert.Equal(message.AttemptCount, envelope.AttemptCount);
    }

    /// <summary>
    ///     The broker message identifier is the outbox row identifier, so a redelivery is recognisable
    ///     as the same message rather than a new one.
    /// </summary>
    [Fact]
    public async Task Reuses_the_outbox_identifier_as_the_broker_message_identifier()
    {
        await using var harness = await Harness.StartAsync();

        var message = NewMessage();
        await harness.Publisher.PublishAsync(message);

        var published = Assert.Single(harness.PublishedEnvelopes());

        Assert.Equal(message.Id, published.Context.MessageId);
    }

    /// <summary>
    ///     The discriminator travels as a header as well as in the body, because routing and
    ///     filtering are expressed in terms of it and a consumer should not have to deserialise the
    ///     payload to filter on it.
    /// </summary>
    [Fact]
    public async Task Publishes_the_message_type_as_a_header()
    {
        await using var harness = await Harness.StartAsync();

        var message = NewMessage();
        await harness.Publisher.PublishAsync(message);

        var published = Assert.Single(harness.PublishedEnvelopes());

        Assert.Equal(
            message.Type,
            published.Context.Headers.Get<string>(MassTransitOutboxPublisher.HeaderNames.Type));
    }

    /// <summary>
    ///     A transport failure propagates. Swallowing it would make the dispatcher record a successful
    ///     delivery and drop the message — the one thing the outbox must never do.
    /// </summary>
    [Fact]
    public async Task Transport_failures_propagate_to_the_dispatcher()
    {
        var endpoint = new Mock<IPublishEndpoint>();

        // Every route MassTransit can take from a publish to the transport is refused, so the test
        // does not depend on which overload the publish helper happens to use.
        endpoint
            .Setup(e => e.Publish(
                It.IsAny<OutboxEnvelope>(),
                It.IsAny<IPipe<PublishContext<OutboxEnvelope>>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        endpoint
            .Setup(e => e.Publish(
                It.IsAny<OutboxEnvelope>(),
                It.IsAny<IPipe<PublishContext>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker unreachable"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Publisher(endpoint.Object).PublishAsync(NewMessage()));
    }

    /// <summary>
    ///     The outbox row is projected onto the wire contract without dropping or reshaping a field.
    /// </summary>
    [Fact]
    public void Envelope_projection_preserves_every_field()
    {
        var message = NewMessage();

        var envelope = OutboxEnvelope.FromOutboxMessage(message);

        Assert.Equal(message.Id, envelope.MessageId);
        Assert.Equal(message.Type, envelope.Type);
        Assert.Equal(message.Content, envelope.Content);
        Assert.Equal(message.CreatedAt, envelope.CreatedAt);
        Assert.Equal(message.AttemptCount, envelope.AttemptCount);
    }

    private static OutboxMessage NewMessage() => new()
    {
        Id = Guid.NewGuid(),
        Type = "PolicyCreated",
        Content = """{"policyId":7}""",
        CreatedAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
        AttemptCount = 2
    };

    private static MassTransitOutboxPublisher Publisher(IPublishEndpoint endpoint)
    {
        return new MassTransitOutboxPublisher(
            endpoint,
            Pipelines(),
            Options.Create(BrokerBudget()),
            NullLogger<MassTransitOutboxPublisher>.Instance);
    }

    /// <summary>
    ///     A broker budget with no retries and a generous timeout, so a test asserting on the
    ///     publisher's own behaviour is not also waiting out a backoff.
    /// </summary>
    private static BrokerOptions BrokerBudget() => new()
    {
        Enabled = true,
        Host = "localhost",
        QueueName = "policy-manager.outbox",
        PublishTimeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    ///     The pipeline with no retries and no breaker, so a test asserting on the publisher's own
    ///     behaviour is not also waiting out a backoff or tripping a circuit.
    /// </summary>
    private static ResiliencePipelineFor<BrokerPipeline> Pipelines()
        => new(ResilienceRegistration.Build(new DependencyResilienceOptions
        {
            MaxRetryAttempts = 0,
            UseJitter = false,
            MinimumThroughput = 100,
            Timeout = TimeSpan.FromSeconds(30)
        }));

    private static DependencyResilienceOptions NoRetryBroker() => new()
    {
        MaxRetryAttempts = 0,
        UseJitter = false,
        MinimumThroughput = 100,
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    ///     A started in-memory bus with the real publisher registered against it, so the assertions
    ///     are about the production class rather than a test double of it.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private Harness(ServiceProvider provider, ITestHarness bus, IOutboxPublisher publisher)
        {
            _provider = provider;
            Bus = bus;
            Publisher = publisher;
        }

        public ITestHarness Bus { get; }

        public IOutboxPublisher Publisher { get; }

        public IReadOnlyList<IPublishedMessage<OutboxEnvelope>> PublishedEnvelopes()
            => Bus.Published.Select<OutboxEnvelope>(CancellationToken.None).ToList();

        public static async Task<Harness> StartAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();
            services.AddMassTransitTestHarness(_ => { });

            var provider = services.BuildServiceProvider(true);

            var bus = provider.GetTestHarness();
            await bus.Start();

            using var scope = provider.CreateScope();

            return new Harness(
                provider,
                bus,
                new MassTransitOutboxPublisher(
                    scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(),
                    Pipelines(),
                    Options.Create(BrokerBudget()),
                    NullLogger<MassTransitOutboxPublisher>.Instance));
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
        }
    }
}
