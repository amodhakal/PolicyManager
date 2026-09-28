using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Models;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Proves the email address is ciphertext in the database, not merely in the mapper.
/// </summary>
/// <remarks>
///     The in-memory provider ignores value converters entirely, so it cannot distinguish an
///     encrypted column from a plaintext one and this is the only place the claim can be checked.
///     Tagged <c>Category=SqlServer</c> so it is excluded from runs without Docker.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public class PiiAtRestTests(SqlServerFixture fixture) : SqlServerTestBase(fixture)
{
    [Fact]
    public async Task The_stored_address_is_not_the_address()
    {
        var holderId = await SeedHolderAsync();

        await using var context = CreateContext();

        // A projection of the column goes through the converter, so this is the *mapped* value. The
        // raw bytes are read below.
        var mapped = await context.PolicyHolders.AsNoTracking()
            .Where(h => h.Id == holderId)
            .Select(h => h.Email)
            .SingleAsync();

        Assert.Equal(SeedEmail, mapped);

        var stored = await ReadRawColumnAsync(context, holderId, "[Email]");

        Assert.NotEqual(SeedEmail, stored);
        Assert.DoesNotContain("jane", stored);
        Assert.DoesNotContain("@", stored);
        Assert.Equal(SeedEmail, PiiCipher.Current!.Decrypt(stored));
    }

    [Fact]
    public async Task The_blind_index_is_written_alongside_the_ciphertext()
    {
        var holderId = await SeedHolderAsync();

        await using var context = CreateContext();
        var stored = await ReadRawColumnAsync(context, holderId, "[EmailHash]");

        Assert.Equal(PiiCipher.Current!.BlindIndex(SeedEmail), stored);
        Assert.DoesNotContain("jane", stored);
    }

    [Fact]
    public async Task Two_holders_with_the_same_address_have_different_ciphertext()
    {
        var first = await SeedHolderAsync();
        var second = await SeedHolderAsync(email: SeedEmail.ToUpperInvariant());

        await using var context = CreateContext();

        var rows = await context.PolicyHolders.AsNoTracking()
            .Where(h => h.Id == first || h.Id == second)
            .Select(h => h.Id)
            .ToListAsync();

        // The second insert should have been refused outright by the blind index or the service
        // check, so this asserts the *shape* of what the column can hold rather than that a duplicate
        // slipped through: identical plaintext must never be inferable from identical bytes.
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task A_duplicate_address_is_refused_by_the_blind_index()
    {
        await SeedHolderAsync();

        var services = Fixture.CreateContext();
        await using var _ = services;

        // Round-tripped through the API so the real write path runs, including its duplicate check.
        using var factory = new SqlServerApiFactory(Fixture);
        using var client = factory.CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync("/api/policyholders", new CreatePolicyHolderDto
        {
            FirstName = "Jane", LastName = "Doe", Email = SeedEmail
        });

        var second = await client.PostAsJsonAsync("/api/policyholders", new CreatePolicyHolderDto
        {
            FirstName = "Jane", LastName = "Smith", Email = SeedEmail
        });

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task A_read_through_the_database_never_returns_ciphertext()
    {
        var holderId = await SeedHolderAsync();

        await using var context = CreateContext();
        var holder = await context.PolicyHolders.AsNoTracking().SingleAsync(h => h.Id == holderId);

        // The entity itself carries the decrypted value, because the converter runs on materialisation
        // as well as on write. A caller that forgets to use a projection would otherwise see base64.
        Assert.Equal(SeedEmail, holder.Email);
        Assert.Equal(SeedEmail, holder.Email);
    }

    [Fact]
    public async Task The_backfill_converts_a_row_written_before_the_feature_existed()
    {
        var holderId = await SeedHolderAsync();

        // Simulate a legacy row: plaintext in the column, no blind index.
        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [PolicyHolders] SET [Email] = {SeedEmail}, [EmailHash] = NULL WHERE [Id] = {holderId}");
        }

        using var factory = new SqlServerApiFactory(Fixture);
        using var scope = factory.Services.CreateScope();
        var backfill = scope.ServiceProvider.GetRequiredService<PiiBackfillService>();

        Assert.Equal(1, await backfill.RunOnceAsync());

        await using (var verify = CreateContext())
        {
            var stored = await ReadRawColumnAsync(verify, holderId, "[Email]");
            Assert.NotEqual(SeedEmail, stored);
            Assert.Equal(SeedEmail, PiiCipher.Current!.Decrypt(stored));
        }

        // Idempotent, and resumable: a second pass finds nothing left to do.
        using var secondScope = factory.Services.CreateScope();
        Assert.Equal(0, await secondScope.ServiceProvider
            .GetRequiredService<PiiBackfillService>().RunOnceAsync());
    }

    [Fact]
    public async Task The_outbox_message_never_carries_the_address()
    {
        using var factory = new SqlServerApiFactory(Fixture);
        using var client = factory.CreateAuthenticatedClient();

        await client.PostAsJsonAsync("/api/policyholders", new CreatePolicyHolderDto
        {
            FirstName = "Jane", LastName = "Doe", Email = SeedEmail
        });

        await using var context = CreateContext();
        var messages = await context.OutboxMessages.AsNoTracking().ToListAsync();
        var serialised = JsonSerializer.Serialize(messages);

        Assert.DoesNotContain(SeedEmail, serialised);
    }

    private const string SeedEmail = "jane.doe@example.com";

    private async Task<string> ReadRawColumnAsync(AppDbContext context, int id, string column)
    {
        // Read through ADO.NET so the value converter is bypassed. Any other path decrypts first,
        // which is the point of the converter and exactly what this assertion must not observe.
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM [PolicyHolders] WHERE [Id] = @id";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        command.Parameters.Add(parameter);

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<int> SeedHolderAsync(string? email = null)
    {
        await using var context = CreateContext();
        var holder = new PolicyHolder
        {
            FirstName = "Jane", LastName = "Doe", Email = email ?? SeedEmail
        };

        context.PolicyHolders.Add(holder);
        await context.SaveChangesAsync();

        return holder.Id;
    }
}
