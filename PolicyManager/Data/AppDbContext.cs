using Microsoft.EntityFrameworkCore;
using PolicyManager.Models;

namespace PolicyManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PolicyHolder> PolicyHolders { get; set; }

    public DbSet<Policy> Policies { get; set; }

    public DbSet<Claim> Claims { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Policy>()
            .HasIndex(p => p.PolicyNumber)
            .IsUnique();
        
        modelBuilder.Entity<PolicyHolder>()
            .HasIndex(p => p.Email)
            .IsUnique();

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