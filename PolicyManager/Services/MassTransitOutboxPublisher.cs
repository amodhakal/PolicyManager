using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Messaging;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     The <see cref="IOutboxPublisher" /> that hands the outbox to a real broker, via MassTransit over
///     AMQP.
/// </summary>
/// <remarks>
///     <para>
///     Registered only when <c>Broker:Enabled</c> is set. Everything above it — claiming, retry
///     scheduling, dead-lettering — is unchanged, because the dispatcher only ever sees this type's
///     contract: publish, or throw. A transport failure surfaces as an exception and the existing
///     policy decides what happens next.
///     </para>
///     <para>
///     A throw is therefore load-bearing. Swallowing it, or returning success, would mark the message
///     processed and drop it on the floor — the one failure the outbox is supposed to make
///     impossible.
///     </para>
/// </remarks>
public sealed class MassTransitOutboxPublisher(
    IPublishEndpoint publishEndpoint,
    IOptions<BrokerOptions> options,
    ILogger<MassTransitOutboxPublisher> logger) : IOutboxPublisher
{
    /// <summary>
    ///     The AMQP source address recorded on every published message, so a consumer can tell which
    ///     application a message came from.
    /// </summary>
    public const string SourceAddress = "policymanager://outbox";

    private readonly BrokerOptions _options = options.Value;

    /// <inheritdoc />
    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var envelope = OutboxEnvelope.FromOutboxMessage(message);

        // Linked rather than passed straight through so the broker's own budget applies even when the
        // caller has no deadline. Without it a publish could outlive the claim lease, and a second
        // processor would pick the same message up while this one is still waiting on the broker.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PublishTimeout);

        await publishEndpoint.Publish(
            envelope,
            context =>
            {
                // The outbox row is the message's identity. Setting it explicitly means the broker
                // message identifier matches the outbox row, so a redelivery is recognisable.
                context.MessageId = envelope.MessageId;
                context.SourceAddress = new Uri(SourceAddress);

                // The discriminator travels as a header as well as in the body, because it is what
                // routing and filtering are expressed in terms of.
                context.Headers.Set(HeaderNames.Type, envelope.Type);
            },
            timeout.Token);

        logger.LogInformation(
            "Outbox message {MessageId} of type {MessageType} published to the broker.",
            message.Id,
            message.Type);
    }

    /// <summary>
    ///     Header names the publisher sets on every message.
    /// </summary>
    public static class HeaderNames
    {
        /// <summary>
        ///     Carries the outbox message type, mirroring the outbox column of the same name.
        /// </summary>
        public const string Type = "PolicyManager.OutboxType";
    }
}
