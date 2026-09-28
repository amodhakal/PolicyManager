using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using PolicyManager.Configuration;
using PolicyManager.Services;

namespace PolicyManager.Messaging;

/// <summary>
///     Wires the transactional outbox to a real message broker — or, deliberately, does not.
/// </summary>
public static class BrokerRegistration
{
    /// <summary>
    ///     Registers the broker transport when <c>Broker:Enabled</c> is set, and nothing when it is
    ///     not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The options are bound either way, so <see cref="BrokerOptions" /> is injectable whether or
    ///     not a broker is configured and a test can assert on the resolved configuration without one
    ///     running. Only the bus is conditional, and that is what keeps a broker out of the default
    ///     build and test path.
    ///     </para>
    ///     <para>
    ///     The logging stand-in is registered with <c>TryAdd</c> before this runs, so a disabled
    ///     broker leaves it in place; the real publisher is added with <c>Add</c>, so an enabled one
    ///     wins by being registered later. The ordering, rather than a <c>Replace</c> call, is what
    ///     expresses the choice.
    ///     </para>
    /// </remarks>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Configuration carrying the <c>Broker</c> section.</param>
    public static IServiceCollection AddOutboxBroker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BrokerOptions>(configuration.GetSection(BrokerOptions.SectionName));

        var options = configuration.GetSection(BrokerOptions.SectionName).Get<BrokerOptions>()
                      ?? new BrokerOptions();

        if (!options.Enabled) return services;

        // Fails at startup rather than at the first outbox message, so a deployment that meant to
        // publish but misconfigured it does not come up healthy while silently discarding events.
        options.Validate();

        services.AddMassTransit(registration =>
        {
            registration.UsingRabbitMq((_, cfg) =>
            {
                cfg.Host(
                    options.Host,
                    (ushort)options.Port,
                    options.VirtualHost,
                    $"{options.Host}-{options.VirtualHost}",
                    host =>
                    {
                        host.Username(options.Username);
                        host.Password(options.Password);
                        host.Heartbeat(options.RequestedHeartbeat);
                        host.RequestedConnectionTimeout(options.RequestedConnectionTimeout);

                        if (options.UseSsl) host.UseSsl(ssl => ssl.ServerName = options.Host);
                    });

                // The queue is declared and bound to the publish exchange here rather than being
                // created implicitly by a consumer. This application produces outbox messages and
                // consumes none of them, so nothing downstream would ever declare the queue — and an
                // unbound queue means the messages reached an exchange and were discarded by the
                // broker, which is precisely the failure this issue exists to close.
                cfg.Publish<OutboxEnvelope>(publish =>
                {
                    publish.Durable = options.Durable;

                    if (options.Durable)
                        publish.BindQueue(options.QueueName, nameof(OutboxEnvelope), _ => { });
                });
            });

            // Readiness, not liveness: the process is running whether or not the broker answers, and
            // restarting it does not bring a broker back. Degraded is enough to fail the probe —
            // a bus still starting, or with an endpoint that has not yet come up, would otherwise
            // take the whole API out of rotation.
            registration.ConfigureHealthCheckOptions(check =>
            {
                check.Name = "message-broker";
                check.MinimalFailureStatus = HealthStatus.Degraded;
                check.Tags.Add("ready");
            });
        });

        services.AddScoped<IOutboxPublisher, MassTransitOutboxPublisher>();

        return services;
    }
}
