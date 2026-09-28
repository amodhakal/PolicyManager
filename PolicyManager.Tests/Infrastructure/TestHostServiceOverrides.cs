using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PolicyManager.Data;
using PolicyManager.Services;

namespace PolicyManager.Tests.Infrastructure;

/// <summary>
///     Shared host-configuration helpers for the <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" />
///     subclasses that substitute a test database for SQL Server.
/// </summary>
/// <remarks>
///     <see cref="Program" /> registers <see cref="AppDbContext" /> against a SQL Server connection
///     string that is not available during tests, so every descriptor that resolves the context has to
///     be removed before a replacement is added. That matching is fragile and repetitive, so it lives
///     here once rather than in each factory.
/// </remarks>
public static class TestHostServiceOverrides
{
    /// <summary>
    ///     Removes the application's <see cref="AppDbContext" /> registrations.
    /// </summary>
    /// <param name="services">The service collection to mutate.</param>
    public static void RemoveAppDbContext(IServiceCollection services)
    {
        var toRemove = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                        || d.ServiceType == typeof(DbContext)
                        || d.ServiceType == typeof(AppDbContext)
                        || (d.ImplementationType?.Name.Contains("AppDbContext") ?? false)
                        || (d.ServiceType.FullName?.Contains("DbContextOptions") ?? false))
            .ToList();

        foreach (var d in toRemove) services.Remove(d);
    }

    /// <summary>
    ///     Removes the outbox polling service so tests observe a quiescent outbox.
    /// </summary>
    /// <remarks>
    ///     The background service drains the outbox on a timer. Left running against a test database
    ///     it would race with assertions about the rows a test just wrote, so it is removed wherever
    ///     the test asserts on outbox contents. The service's own behaviour is covered separately in
    ///     <c>OutboxProcessorBackgroundServiceTests</c>, where running it is the point.
    /// </remarks>
    /// <param name="services">The service collection to mutate.</param>
    public static void RemoveOutboxProcessor(IServiceCollection services)
    {
        services.RemoveAll<IHostedService>();
        services.RemoveAll<OutboxProcessorBackgroundService>();
    }
}
