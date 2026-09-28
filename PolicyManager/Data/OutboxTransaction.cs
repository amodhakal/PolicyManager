using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PolicyManager.Models;

namespace PolicyManager.Data;

/// <summary>
///     Writes an entity change and its corresponding outbox message inside a single database transaction.
/// </summary>
/// <remarks>
///     The transactional outbox guarantee requires the entity row and the message row to commit together, or not at
///     all. Serializing the payload before the entity has been inserted produces a message whose <c>Id</c> is always
///     <c>0</c>, because the identifier is only assigned by the database on insert. These helpers therefore flush the
///     entity first, serialize the payload once the generated identifier is known, and only then write the message —
///     all inside one explicit transaction so the two writes remain atomic.
/// </remarks>
public static class OutboxTransaction
{
    /// <summary>
    ///     Persists a newly created entity together with an outbox message describing the change.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity being created.</typeparam>
    /// <param name="context">The context owning the transaction.</param>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="messageType">The outbox message type discriminator.</param>
    /// <param name="payloadFactory">
    ///     Builds the message payload from the saved entity, so it observes the generated identifier.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public static async Task AddWithOutboxAsync<TEntity>(
        this AppDbContext context,
        TEntity entity,
        string messageType,
        Func<TEntity, object> payloadFactory,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        context.Add(entity);
        await context.SaveWithOutboxAsync(entity, messageType, payloadFactory, cancellationToken);
    }

    /// <summary>
    ///     Persists pending changes to an already tracked entity together with an outbox message describing the change.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity being changed.</typeparam>
    /// <param name="context">The context owning the transaction.</param>
    /// <param name="entity">The tracked entity whose pending changes should be flushed.</param>
    /// <param name="messageType">The outbox message type discriminator.</param>
    /// <param name="payloadFactory">
    ///     Builds the message payload from the saved entity, so it observes any identifier or generated value.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public static async Task SaveWithOutboxAsync<TEntity>(
        this AppDbContext context,
        TEntity entity,
        string messageType,
        Func<TEntity, object> payloadFactory,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = messageType,
            Content = JsonSerializer.Serialize(payloadFactory(entity))
        });
        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
