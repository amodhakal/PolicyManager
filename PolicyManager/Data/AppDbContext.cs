using Microsoft.EntityFrameworkCore;
using PolicyManager.Models;

namespace PolicyManager.Data;

/// <summary>
///     Entity Framework DbContext for the Policy Manager application.
/// </summary>
/// <remarks>
///     Manages database connections and entity mappings for PolicyHolder, Policy, Claim, and OutboxMessage entities.
///     Configures relationships, indexes, and cascade delete behaviors.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
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
            .HasOne(p => p.PolicyHolder)
            .WithMany(ph => ph.Policies)
            .HasForeignKey(p => p.PolicyHolderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Claim>()
            .HasOne(c => c.Policy)
            .WithMany(p => p.Claims).HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
