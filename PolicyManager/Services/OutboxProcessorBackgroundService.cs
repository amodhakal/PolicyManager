using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PolicyManager.Data;

namespace PolicyManager.Services;

/// <summary>
/// Background service that polls the transactional outbox table and dispatches unprocessed messages.
/// </summary>
public class OutboxProcessorBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxProcessorBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.logInformation("Outbox Processor Background Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var messages = await dbContext.OutboxMessages
                    .Where(m => m.ProcessedAt == null)
                    .OrderBy(m => m.CreatedAt)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var message in messages)
                {
                    try
                    {
                        // Simulate publishing to message broker (RabbitMQ / MassTransit)
                        logger.LogInformation("Processing outbox message {MessageId} of type {MessageType}", message.Id, message.Type);

                        // Here we could resolve IBus / IPublishEndpoint and publish message.content
                        // For demonstration and transactional outbox guarantee, we mark as processed.
                        message.ProcessedAt = DateTime.UtcNow;
                        message.Error = null;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to process outbox message {MessageId}", message.Id);
                        message.Error = ex.Message;
                    }
                }

                if (messages.Count > 0)
                {
                    await dbContext.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while processing outbox messages.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
