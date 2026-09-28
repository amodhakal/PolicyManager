using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PolicyManager.Services;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
<<<<<<< HEAD
using PolicyManager.Models.Enums;
using PolicyManager.Services;
=======
>>>>>>> efdfa10 (feat: tune the indexes to the queries the app actually issues)
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Tests for database behaviour the EF Core in-memory provider does not model.
/// </summary>
/// <remarks>
///     Every test here fails to compile in spirit if run against <c>UseInMemoryDatabase</c>: the
///     in-memory store builds its schema from the model and enforces no unique indexes, no foreign
///     keys, no column precision, no string length limits and no transactions. A suite restricted to
///     that provider will report green while the application is broken against SQL Server.
///     <para>
///     These assertions are about the <em>schema</em>, not about API status codes, so they stay valid
///     as the API's error handling is tightened.
///     </para>
/// </remarks>
[Collection(SqlServerCollection.Name)]
public class SqlServerConstraintTests : SqlServerTestBase
{
    /// <summary>
    ///     SQL Server's error number for a foreign key constraint violation.
    /// </summary>
    private const int ForeignKeyViolation = 547;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SqlServerConstraintTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared container fixture.</param>
    public SqlServerConstraintTests(SqlServerFixture fixture) : base(fixture)
    {
    }

    /// <summary>
    ///     The unique index on <c>PolicyHolder.Email</c> rejects a second holder with the same address.
    /// </summary>
    [Fact]
    public async Task Duplicate_email_is_rejected_by_the_unique_index()
    {
        await using var context = CreateContext();

        context.PolicyHolders.Add(new PolicyHolder
            { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" });
        await context.SaveChangesAsync();

        context.PolicyHolders.Add(new PolicyHolder
            { FirstName = "Jane", LastName = "Roe", Email = "jane@example.com" });

        // The in-memory provider has no unique index, so this SaveChangesAsync would succeed there.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    ///     The foreign key from <c>Policies</c> to <c>PolicyHolders</c> rejects an unknown holder.
    /// </summary>
    [Fact]
    public async Task Policy_for_an_unknown_holder_is_rejected_by_the_foreign_key()
    {
        await using var context = CreateContext();

        context.Policies.Add(new Policy
        {
            PolicyHolderId = 987_654, Premium = 100m, Status = Models.Enums.PolicyStatus.Active,
            Type = Models.Enums.PolicyType.Auto, PolicyNumber = "POL-2026-000901",
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });

        // The service checks the holder exists before inserting, so this is now the race backstop:
        // the only way to reach it is for the holder to disappear between the check and the write.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    ///     Deleting a policyholder who still has claims is refused, and nothing is destroyed.
    /// </summary>
    /// <remarks>
    ///     This is the cascade that was destroying financial records. The claim carries the
    ///     adjudication trail - who decided, when, and why - so losing it to a contact-record delete
    ///     loses the audit history with no record that it ever existed. The refusal can come from the
    ///     change tracker (which will not sever a required relationship) or from the database; either
    ///     way nothing is written, and what matters is that the rows are all still there afterwards.
    /// </remarks>
    [Fact]
    public async Task Deleting_a_holder_with_claims_is_refused_and_nothing_is_destroyed()
    {
        await using var context = CreateContext();
        var (holderId, claimId) = await SeedHolderPolicyAndClaimAsync(context);

        var failure = await Record.ExceptionAsync(async () =>
        {
            context.PolicyHolders.Remove(context.PolicyHolders.Local.Single(h => h.Id == holderId));
            await context.SaveChangesAsync();
        });

        Assert.NotNull(failure);
        context.ChangeTracker.Clear();

        Assert.Equal(ClaimStatus.Approved, await context.Claims.AsNoTracking()
            .Where(c => c.Id == claimId).Select(c => c.Status).SingleAsync());
        Assert.Equal(1, await context.PolicyHolders.CountAsync());
        Assert.Equal(1, await context.Policies.CountAsync());
    }

    /// <summary>
    ///     The database itself rejects the delete, not only the change tracker.
    /// </summary>
    /// <remarks>
    ///     Issued as raw SQL so the claim about the schema is tested directly. Anything going through
    ///     the context can be refused client-side, which would leave a test passing against a schema
    ///     that had no constraint at all - the case the in-memory provider can never catch and the
    ///     reason these tests run against a real server.
    /// </remarks>
    [Fact]
    public async Task The_foreign_key_rejects_a_holder_delete_bypassing_the_change_tracker()
    {
        await using var context = CreateContext();
        var (holderId, claimId) = await SeedHolderPolicyAndClaimAsync(context);

        var failure = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM [PolicyHolders] WHERE [Id] = {0}", holderId));

        Assert.Equal(ForeignKeyViolation, failure.Number);
        Assert.True(await context.Claims.AsNoTracking().AnyAsync(c => c.Id == claimId));
    }

    /// <summary>
    ///     The database itself rejects deleting a policy out from under its claims.
    /// </summary>
    [Fact]
    public async Task The_foreign_key_rejects_a_policy_delete_bypassing_the_change_tracker()
    {
        await using var context = CreateContext();
        var (_, claimId) = await SeedHolderPolicyAndClaimAsync(context);
        var policyId = await context.Claims.AsNoTracking().Where(c => c.Id == claimId)
            .Select(c => c.PolicyId).SingleAsync();

        var failure = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM [Policies] WHERE [Id] = {0}", policyId));

        Assert.Equal(ForeignKeyViolation, failure.Number);
        Assert.True(await context.Claims.AsNoTracking().AnyAsync(c => c.Id == claimId));
    }

    /// <summary>
    ///     A holder with nothing attached is still deletable, so the guard blocks destruction rather
    ///     than blocking the endpoint outright.
    /// </summary>
    [Fact]
    public async Task Deleting_an_unattached_holder_is_allowed()
    {
        await using var context = CreateContext();

        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        context.PolicyHolders.Remove(holder);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        Assert.Empty(await context.PolicyHolders.AsNoTracking().ToListAsync());
    }

    /// <summary>
    ///     Seeds a policyholder, a policy under them, and one adjudicated claim against it.
    /// </summary>
    /// <param name="context">The context to seed through.</param>
    /// <returns>The policyholder's identifier and the claim's identifier.</returns>
    private static async Task<(int HolderId, int ClaimId)> SeedHolderPolicyAndClaimAsync(AppDbContext context)
    {
        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        var policy = new Policy
        {
            PolicyHolderId = holder.Id, Premium = 500m, Status = Models.Enums.PolicyStatus.Active,
            Type = Models.Enums.PolicyType.Auto,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        };
        context.Policies.Add(policy);
        await context.SaveChangesAsync();

        var claim = new Claim
        {
            PolicyId = policy.Id, Amount = 250m, Status = ClaimStatus.Approved, DecidedBy = "adjuster-1"
        };
        context.Claims.Add(claim);
        await context.SaveChangesAsync();

        return (holder.Id, claim.Id);
    }

    /// <summary>
    ///     The foreign key from <c>Claims</c> to <c>Policies</c> rejects an unknown policy.
    /// </summary>
    [Fact]
    public async Task Claim_for_an_unknown_policy_is_rejected_by_the_foreign_key()
    {
        await using var context = CreateContext();

        context.Claims.Add(new Claim { PolicyId = 987_654, Amount = 50m, ClaimNumber = "CLM-2026-000901" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    ///     <c>decimal(10,2)</c> rounds a premium carrying more precision than the column can store.
    /// </summary>
    [Fact]
    public async Task Premium_is_rounded_to_the_column_scale()
    {
        await using var context = CreateContext();

        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        context.Policies.Add(new Policy
        {
            PolicyHolderId = holder.Id, Premium = 1234.567m, Status = Models.Enums.PolicyStatus.Active,
            Type = Models.Enums.PolicyType.Auto,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var stored = await context.Policies.AsNoTracking().SingleAsync();
        Assert.Equal(1234.57m, stored.Premium);
    }

    /// <summary>
    ///     <c>decimal(10,2)</c> cannot hold a claim amount beyond its 99,999,999.99 ceiling.
    /// </summary>
    [Fact]
    public async Task Claim_amount_beyond_the_column_capacity_is_rejected()
    {
        await using var context = CreateContext();

        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        context.Policies.Add(new Policy
        {
            PolicyHolderId = holder.Id, Premium = 100m, Status = Models.Enums.PolicyStatus.Active,
            Type = Models.Enums.PolicyType.Auto, PolicyNumber = "POL-2026-000902",
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });
        await context.SaveChangesAsync();

        var policyId = await context.Policies.Select(p => p.Id).SingleAsync();

        context.Claims.Add(new Claim
        {
            PolicyId = policyId, Amount = 100_000_000m, ClaimNumber = "CLM-2026-000902"
        });

        // Overflows the column. The in-memory provider has no such limit and would store it happily.
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    ///     <c>nvarchar(100)</c> on <c>FirstName</c> rejects an over-long value rather than truncating it.
    /// </summary>
    [Fact]
    public async Task Over_long_first_name_is_rejected_by_the_column_length()
    {
        await using var context = CreateContext();

        context.PolicyHolders.Add(new PolicyHolder
            { FirstName = new string('a', 101), LastName = "Doe", Email = "jane@example.com" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    ///     The outbox payload carries the identifier the database actually assigned.
    /// </summary>
    /// <remarks>
    ///     This is the regression test for serializing the message before the insert, which published
    ///     an <c>Id</c> of <c>0</c> in every message.
    /// </remarks>
    [Fact]
    public async Task Outbox_payload_carries_the_assigned_entity_id()
    {
        await using var context = CreateContext();

        var holder = new PolicyHolder { FirstName = "Jane", LastName = "Doe", Email = "jane@example.com" };
        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        var service = new PoliciesService(context, new BusinessNumberGenerator(
            context, TimeProvider.System, NullLogger<BusinessNumberGenerator>.Instance));
        var policyId = await service.Create(new CreatePolicyDto
        {
            PolicyHolderId = holder.Id, Premium = 500m, Type = Models.Enums.PolicyType.Auto,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });

        var message = await context.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == "PolicyCreated");

        using var payload = JsonDocument.Parse(message.Content);
        Assert.Equal(policyId, payload.RootElement.GetProperty("Id").GetInt32());
        Assert.NotEqual(0, payload.RootElement.GetProperty("Id").GetInt32());
    }

    /// <summary>
    ///     A failed entity write leaves no outbox row behind.
    /// </summary>
    /// <remarks>
    ///     The entity and the message must commit together. If the entity insert fails, the message
    ///     must never appear — otherwise consumers would be told about a change that did not happen.
    /// </remarks>
    [Fact]
    public async Task Failed_entity_write_leaves_no_outbox_message()
    {
        await using var context = CreateContext();

        var service = new PoliciesService(context, new BusinessNumberGenerator(
            context, TimeProvider.System, NullLogger<BusinessNumberGenerator>.Instance));

        // PolicyHolderId points at nothing, so the foreign key rejects the insert.
        await Assert.ThrowsAnyAsync<Exception>(() => service.Create(new CreatePolicyDto
        {
            PolicyHolderId = 987_654, Premium = 500m, Type = Models.Enums.PolicyType.Auto,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        }));

        Assert.Empty(await context.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await context.Policies.AsNoTracking().ToListAsync());
    }
}
