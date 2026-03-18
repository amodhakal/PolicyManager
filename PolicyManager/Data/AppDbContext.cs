using Microsoft.EntityFrameworkCore;
using PolicyManager.Models;

namespace PolicyManager.Data;

/// <summary>
///     Entity Framework DbContext for the Policy Manager application.
/// </summary>
/// <remarks>
///     Manages database connections and entity mappings for PolicyHolder, Policy, and Claim entities.
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

        modelBuilder.Entity<Policy>()
            .HasOne(p => p.PolicyHolder)
            .WithMany(ph => ph.Policies)
            .HasForeignKey(p => p.PolicyHolderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Claim>()
            .HasOne(c => c.Policy)
            .WithMany(p => p.Claims).HasForeignKey(c => c.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}