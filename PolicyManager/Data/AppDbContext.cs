using Microsoft.EntityFrameworkCore;
using PolicyManager.Models;
using PolicyManager.Services;

namespace PolicyManager.Data;

/// <summary>
///     Entity Framework DbContext for the Policy Manager application.
/// </summary>
/// <remarks>
///     Manages database connections and entity mappings for PolicyHolder, Policy, Claim, and OutboxMessage entities.
///     Configures relationships, indexes, and cascade delete behaviors.
/// </remarks>
public class AppDbContext : DbContext
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AppDbContext" /> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="timeProvider">The clock used to stamp audit timestamps.</param>
    /// <param name="currentUser">Resolves who the current write is attributable to.</param>
    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
        : base(options)
    {
        TimeProvider = timeProvider;
        CurrentUser = currentUser;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AppDbContext" /> class with the system clock
    ///     and no ambient caller.
    /// </summary>
    /// <remarks>
    ///     Used by tests and by tooling that constructs a context directly rather than through
    ///     dependency injection. It makes those callers record <c>system</c> as the actor, which is
    ///     the honest answer: nothing authenticated made the change.
    /// </remarks>
    /// <param name="options">The context options.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : this(options, TimeProvider.System, new HttpContextCurrentUser(new HttpContextAccessor()))
    {
    }

    /// <summary>
    ///     Gets the clock used to stamp audit timestamps.
    /// </summary>
    protected TimeProvider TimeProvider { get; }

    /// <summary>
    ///     Gets the resolver for the actor recorded on audited rows.
    /// </summary>
    protected ICurrentUser CurrentUser { get; }

    /// <summary>
    ///     Gets or sets the collection of policyholders.
    /// </summary>
    public DbSet<PolicyHolder> PolicyHolders { get; set; }

    /// <summary>
    ///     Gets or sets the collection of policies.
    /// </summary>
    public DbSet<Policy> Policies { get; set; }

    /// <summary>
    ///     Gets or sets the collection of claims.
    /// </summary>
    public DbSet<Claim> Claims { get; set; }

    /// <summary>
    ///     Gets or sets the collection of outbox messages for transactional messaging.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    ///     Fills in the audit columns for every added or modified audited entity about to be written.
    /// </summary>
    /// <remarks>
    ///     Done here rather than in each service so that a write path cannot forget it. The explicit
    ///     <c>IsModified</c> on the update side is what keeps a genuine edit from being silently
    ///     dropped: if the same operator re-saves the same value, EF detects no change to those
    ///     properties, and without the flag the timestamp of the last real edit would be lost.
    /// </remarks>
    private void StampAuditFields()
    {
        var now = TimeProvider.GetUtcNow().UtcDateTime;
        var actor = CurrentUser.Actor;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = actor;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = actor;
                    entry.Property(nameof(IAuditableEntity.UpdatedAt)).IsModified = true;
                    entry.Property(nameof(IAuditableEntity.UpdatedBy)).IsModified = true;
                    break;
            }
        }
    }

    /// <summary>
    ///     Configures the entity model and relationships.
    /// </summary>
    /// <param name="modelBuilder">The model builder to configure entities.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Policy>()
            .HasIndex(p => p.PolicyNumber)
            .IsUnique();

        modelBuilder.Entity<Policy>()
            .HasIndex(p => p.Status);

        modelBuilder.Entity<Policy>()
            .HasIndex(p => p.PolicyHolderId);

        modelBuilder.Entity<PolicyHolder>()
            .HasIndex(p => p.Email)
            .IsUnique();

        modelBuilder.Entity<Claim>()
            .HasIndex(c => c.PolicyId);

        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(o => o.ProcessedAt);

        // Serves the dispatcher's poll predicate: live, undelivered rows that are due and unclaimed.
        // A filtered index keeps processed and dead-lettered rows out of the index entirely, so the
        // structure stays small no matter how much history the table accumulates.
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(o => new { o.ProcessedAt, o.NextAttemptAt })
            .HasFilter("[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Pending");

        // Claims are the record of money that has been claimed and paid out, and they carry the
// adjudication trail: who decided, when, and why. Cascading a delete from a policyholder through
// their policies into their claims destroyed that history as a side effect of removing a contact
// record, with no prompt and no trace of what was lost - and there is no API that reads a claim
// back once its policy is gone, so nothing downstream would even notice. Both relationships
// restrict instead: the database refuses the delete while dependents exist, and the caller is told
// why. Retirement of a holder or a policy is a status change (a policy is cancelled, not deleted);
// this only removes the silent destruction of financial records.
modelBuilder.Entity<Policy>()
        // Configured per CLR type rather than through the interface: IsRowVersion is provider
        // specific (a SQL Server rowversion column), so it has to be applied to each mapped entity
        // the provider will recognise it on.
        ConfigureConcurrencyToken<Policy>(modelBuilder);
        ConfigureConcurrencyToken<Claim>(modelBuilder);
        ConfigureConcurrencyToken<PolicyHolder>(modelBuilder);

        modelBuilder.Entity<Policy>()
            .HasOne(p => p.PolicyHolder)
            .WithMany(ph => ph.Policies)
            .HasForeignKey(p => p.PolicyHolderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Claim>()
            .HasOne(c => c.Policy)
            .WithMany(p => p.Claims).HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    ///     Marks an entity's <see cref="IAuditableEntity.RowVersion" /> as a database-maintained
    ///     concurrency token.
    /// </summary>
    /// <typeparam name="TEntity">The audited entity type.</typeparam>
    /// <param name="modelBuilder">The model builder to configure.</param>
    private static void ConfigureConcurrencyToken<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IAuditableEntity
    {
        modelBuilder.Entity<TEntity>()
            .Property(nameof(IAuditableEntity.RowVersion))
            .IsRowVersion();
    }
}
