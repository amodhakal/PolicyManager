using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Models.Enums;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Covers the sequential business numbers that replaced the GUIDs.
/// </summary>
public class BusinessNumberGeneratorTests : ServiceTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_number_of_a_kind_is_one()
    {
        var generator = CreateGenerator();

        var number = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);

        Assert.Equal("POL-2026-000001", number);
    }

    [Fact]
    public async Task Numbers_increase_with_each_call()
    {
        var generator = CreateGenerator();

        var first = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);
        var second = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);
        var third = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);

        Assert.Equal("POL-2026-000001", first);
        Assert.Equal("POL-2026-000002", second);
        Assert.Equal("POL-2026-000003", third);
    }

    [Fact]
    public async Task Each_kind_has_its_own_series()
    {
        var generator = CreateGenerator();

        var policy = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);
        var claim = await generator.NextAsync(BusinessNumberGenerator.ClaimKind);
        var nextPolicy = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);

        Assert.Equal("POL-2026-000001", policy);
        Assert.Equal("CLM-2026-000001", claim);
        Assert.Equal("POL-2026-000002", nextPolicy);
    }

    [Fact]
    public async Task A_new_year_starts_a_new_series()
    {
        var clock = new MutableTimeProvider(Now);
        var generator = CreateGenerator(clock);

        var before = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);
        clock.Now = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var after = await generator.NextAsync(BusinessNumberGenerator.PolicyKind);

        Assert.Equal("POL-2026-000001", before);
        Assert.Equal("POL-2027-000001", after);
    }

    [Fact]
    public async Task Allocation_survives_the_context_being_rebuilt()
    {
        // Each call gets a fresh context over the same store, the way a second process or a
        // later request would. A counter cached in memory would restart here and reissue a number.
        var database = Guid.NewGuid().ToString();
        var first = await AllocateInNewContextAsync(database, Now);
        var second = await AllocateInNewContextAsync(database, Now);

        Assert.Equal("POL-2026-000001", first);
        Assert.Equal("POL-2026-000002", second);
    }

    [Fact]
    public async Task The_reservation_is_recorded_in_the_counter_row()
    {
        var generator = CreateGenerator();

        await generator.NextAsync(BusinessNumberGenerator.PolicyKind);
        await generator.NextAsync(BusinessNumberGenerator.PolicyKind);

        var sequence = await Context.BusinessNumberSequences.SingleAsync();
        Assert.Equal(BusinessNumberGenerator.PolicyKind, sequence.Kind);
        Assert.Equal(2026, sequence.Year);
        Assert.Equal(2, sequence.Current);
        // The counter is an audited entity, so even the allocation itself leaves a trace.
        Assert.Equal("system", sequence.CreatedBy);
    }

    [Fact]
    public async Task A_blank_kind_is_rejected()
    {
        var generator = CreateGenerator();

        await Assert.ThrowsAsync<ArgumentException>(() => generator.NextAsync("  "));
    }

    [Fact]
    public async Task A_created_policy_is_given_a_policy_number()
    {
        var holder = await SeedHolderEntityAsync();
        var service = new PoliciesService(Context, Numbers);

        var id = await service.Create(new CreatePolicyDto
        {
            Type = PolicyType.Auto, PolicyHolderId = holder.Id, Premium = 500m,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2027, 1, 1)
        });

        var policy = await Context.Policies.FindAsync(id);
        Assert.StartsWith("POL-", policy!.PolicyNumber);
        Assert.Matches(@"^POL-\d{4}-\d{6}$", policy.PolicyNumber);
    }

    [Fact]
    public async Task A_filed_claim_is_given_a_claim_number()
    {
        var holder = await SeedHolderEntityAsync();
        var policy = new Policy
        {
            PolicyHolderId = holder.Id, Premium = 500m, Status = PolicyStatus.Active,
            PolicyNumber = "POL-2026-000500"
        };

        Context.Policies.Add(policy);
        await Context.SaveChangesAsync();

        var service = new ClaimsService(Context, Numbers);
        var id = await service.Create(new CreateClaimDto { PolicyId = policy.Id, Amount = 100m });

        var claim = await Context.Claims.FindAsync(id);
        Assert.Matches(@"^CLM-\d{4}-\d{6}$", claim!.ClaimNumber);
    }

    [Fact]
    public void A_reserved_ordinal_is_zero_padded_to_six_digits()
    {
        Assert.Equal("POL-2026-000001", BusinessNumberGenerator.Format(BusinessNumberGenerator.PolicyKind, 2026, 1));
        Assert.Equal("POL-2026-123456", BusinessNumberGenerator.Format(BusinessNumberGenerator.PolicyKind, 2026, 123_456));
    }

    private BusinessNumberGenerator CreateGenerator(TimeProvider? clock = null)
        => new(Context, clock ?? new FixedTimeProvider(Now), NullLogger<BusinessNumberGenerator>.Instance);

    private static async Task<string> AllocateInNewContextAsync(string database, DateTimeOffset now)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(database)
            .Options;

        await using var context = new AppDbContext(options);
        var generator = new BusinessNumberGenerator(
            context, new FixedTimeProvider(now), NullLogger<BusinessNumberGenerator>.Instance);

        return await generator.NextAsync(BusinessNumberGenerator.PolicyKind);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
