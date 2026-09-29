using Microsoft.EntityFrameworkCore;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
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

    /// <summary>
    ///     Gets or sets the counters behind the human-readable business numbers.
    /// </summary>
    public DbSet<BusinessNumberSequence> BusinessNumberSequences { get; set; }

    /// <summary>
    ///     Gets or sets the log of reads of personally identifiable information.
    /// </summary>
    public DbSet<PiiAccessAudit> PiiAccessAudits { get; set; }

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

                    if (entry.Entity is PolicyHolder holder) StampBlindIndex(holder);

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
    ///     Fills in the blind index of a policyholder about to be inserted.
    /// </summary>
    /// <remarks>
    ///     Computed here rather than through a value converter, because a converter is applied to
    ///     both sides of a comparison: a query for <c>EmailHash == x</c> would search for the hash
    ///     <em>of</em> <c>x</c> and find nothing, which would silently disable the duplicate-address
    ///     check. Stamping on save is also what makes it correct on every write path, including ones
    ///     that do not go through a service.
    /// </remarks>
    private void StampBlindIndex(PolicyHolder holder)
    {
        var cipher = PiiCipher.Current;

        if (cipher is not null)
            holder.EmailHash = cipher.BlindIndex(holder.Email);
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

        // Status is the only filter the policies list supports, and every branch of its ORDER BY
        // ends in Id. Leading on Status makes the filtered COUNT and the filtered page both seeks
        // instead of a scan of the whole table; the Id tail means the page's tie-breaker is already
        // in index order, so no sort is needed to satisfy it.
        modelBuilder.Entity<Policy>()
            .HasIndex(p => new { p.Status, p.Id })
            .HasDatabaseName("IX_Policies_Status_Id");

        // Same shape for the holder-scoped listing and the policyHolderId sort branch.
        modelBuilder.Entity<Policy>()
            .HasIndex(p => new { p.PolicyHolderId, p.Id })
            .HasDatabaseName("IX_Policies_PolicyHolderId_Id");

        // The address is stored encrypted, and the ciphertext is randomised, so it cannot be indexed
        // or compared. The unique constraint therefore runs over the keyed hash, which is
        // deterministic and safe to store beside it. See PiiCipher for why it is keyed rather than
        // merely hashed.
        modelBuilder.Entity<PolicyHolder>()
            .Property(p => p.Email)
            .HasConversion(
                email => PiiCipher.Current!.Encrypt(email),
                stored => PiiCipher.Current!.Decrypt(stored));

        // Not unique yet. Uniqueness is the second phase (migration EnforcePiiBlindIndex), applied
        // once the backfill has given every row a blind index; declaring it here before then would
        // make EF want to build the unique index over rows that are still NULL.
        modelBuilder.Entity<PolicyHolder>()
            .HasIndex(p => p.EmailHash)
            .HasDatabaseName("IX_PolicyHolder_EmailHash");

        // Replaces a plain (LastName) index, which could not supply the Id tie-breaker the list
        // appends to every ORDER BY and so had to sort the candidate rows before paging them.
        modelBuilder.Entity<PiiAccessAudit>()
            .HasIndex(a => new { a.PolicyHolderId, a.OccurredAt })
            .HasDatabaseName("IX_PiiAccessAudits_Holder_OccurredAt");

        // Retained so a disclosure review can answer "who read this holder's data" in date order
        // without scanning the whole log, which is the one query this table exists to answer.
        modelBuilder.Entity<PiiAccessAudit>()
            .HasIndex(a => a.OccurredAt)
            .HasDatabaseName("IX_PiiAccessAudits_OccurredAt");

        modelBuilder.Entity<PolicyHolder>()
            .HasIndex(p => new { p.LastName, p.Id })
            .HasDatabaseName("IX_PolicyHolders_LastName_Id");

        // Replaces a plain (PolicyId) index. Every claim query by policy — the coverage sum below
        // and any per-policy listing — also constrains Status, and Amount has to come back for the
        // sum, so all three are keys rather than the PolicyId alone.
        //
        // Filtered to the statuses that still reserve coverage. Denied claims are excluded from the
        // index entirely: the sum never reads them, and they only accumulate, so keeping them would
        // grow the structure without ever serving a query. The predicate is written with the numeric
        // value, not the enum member name — SQL has no idea what "Denied" is.
        modelBuilder.Entity<Claim>()
            .HasIndex(c => new { c.PolicyId, c.Amount })
            .HasFilter($"[Status] <> {(int)ClaimStatus.Denied}")
            .HasDatabaseName("IX_Claims_Coverage");

        // Replaces a plain (PolicyId) index for the same reason as IX_Claims_Coverage, but for the
        // recency listing an adjuster actually runs: ordered claims, newest first.
        modelBuilder.Entity<Claim>()
            .HasIndex(c => new { c.FiledAt, c.Id })
            .HasDatabaseName("IX_Claims_FiledAt_Id");

        // Soft-deleted rows stay in the table but are invisible to every ordinary read, and the
        // filter is applied once here rather than remembered at each query. A restore reaches past it
        // deliberately, with IgnoreQueryFilters().
        //
        // Claims are filtered globally because nothing navigates *to* a claim and expects it to be
        // there. Policyholders are not: a policy projects its holder's name, and a filter here would
        // make that name null for the policies of a deleted holder. Those policies stay in the book,
        // so the holder behind them is still named. PolicyHoldersService applies the filter to its own
        // reads instead, and PoliciesService.GetByPolicyHolder applies it to the holder's existence
        // check, so a deleted policyholder is a 404 everywhere they are addressed directly.
        //
        // No index is added for the flag. Nearly every row is undeleted, so an index over it would not
        // be selective on a read, and a restore already seeks the primary key.
        modelBuilder.Entity<Claim>()
            .HasQueryFilter(c => !c.IsDeleted);

        // The dispatcher's poll. Keyed on CreatedAt because that is the ORDER BY, so the batch is a
        // forward walk of the live rows and the engine can stop as soon as it has enough. A filtered
        // index keeps processed and dead-lettered rows out entirely, so the structure stays small
        // however much history the table accumulates.
        //
        // The two "due and unclaimed" predicates cannot be keys: they are OR-ed against NULL, which
        // is not sargable, so they are applied as a residual filter over the live rows instead.
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(o => o.CreatedAt)
            .HasFilter("[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Pending");

        // Claims are the record of money that has been claimed and paid out, and they carry the
        // adjudication trail: who decided, when, and why. Cascading a delete from a policyholder
        // through their policies into their claims destroyed that history as a side effect of
        // removing a contact record, with no prompt and no trace of what was lost - and there is no
        // API that reads a claim back once its policy is gone, so nothing downstream would even
        // notice. Both relationships restrict instead: the database refuses the delete while
        // dependents exist, and the caller is told why. Retirement of a holder or a policy is a
        // status change (a policy is cancelled, not deleted); this only removes the silent
        // destruction of financial records.
        modelBuilder.Entity<Policy>()
            .HasOne(p => p.PolicyHolder)
            .WithMany(ph => ph.Policies)
            .HasForeignKey(p => p.PolicyHolderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Claim>()
            .HasOne(c => c.Policy)
            .WithMany(p => p.Claims)
            .HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Restrict);

        // The dispatcher's second read: the rows this instance just won the claim on. Nothing
        // indexed LockToken, so every batch that claimed anything finished with a full table scan to
        // collect them. Rarely true and almost always unique, so a filtered index is both smaller
        // and cheaper than a general one.
        modelBuilder.Entity<OutboxMessage>()
            .HasIndex(o => o.LockedUntil)
            .HasFilter("[LockToken] IS NOT NULL")
            .HasDatabaseName("IX_OutboxMessages_Claimed");

        // Configured per CLR type rather than through the interface: IsRowVersion is provider
        // specific (a SQL Server rowversion column), so it has to be applied to each mapped entity
        // the provider will recognise it on.
        ConfigureConcurrencyToken<Policy>(modelBuilder);
        ConfigureConcurrencyToken<Claim>(modelBuilder);
        ConfigureConcurrencyToken<PolicyHolder>(modelBuilder);
        ConfigureConcurrencyToken<BusinessNumberSequence>(modelBuilder);

        // Composite rather than a surrogate key: the pair is what uniquely identifies a counter, and
        // it is the pair the allocation read filters on.
        modelBuilder.Entity<BusinessNumberSequence>()
            .HasKey(s => new { s.Kind, s.Year });

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
