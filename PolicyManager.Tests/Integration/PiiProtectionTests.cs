using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.DTOs;
using PolicyManager.Services;
using PolicyManager.Tests.Infrastructure;

namespace PolicyManager.Tests.Integration;

/// <summary>
///     Covers the three protections applied to policyholder email addresses: access control,
///     encryption at rest, and an audit trail of every read.
/// </summary>
public class PiiProtectionTests : ApiIntegrationTestBase
{
    [Fact]
    public async Task An_adjuster_sees_the_address_in_the_clear()
    {
        var holderId = await SeedHolderAsync();

        var holder = await GetAsRoleAsync(holderId, PolicyRoles.Adjuster);

        Assert.Equal(DefaultHolder.Email, holder.Email);
    }

    [Fact]
    public async Task An_admin_sees_the_address_in_the_clear()
    {
        var holderId = await SeedHolderAsync();

        var holder = await GetAsRoleAsync(holderId, PolicyRoles.Admin);

        Assert.Equal(DefaultHolder.Email, holder.Email);
    }

    [Fact]
    public async Task An_agent_gets_the_record_with_the_address_masked()
    {
        var holderId = await SeedHolderAsync();

        var holder = await GetAsRoleAsync(holderId, PolicyRoles.Agent);

        // An agent files a claim on the holder's behalf and has no need for the address. Returning it
        // anyway would mean a compromised agent account discloses contact details for the whole book.
        Assert.NotEqual(DefaultHolder.Email, holder.Email);
        Assert.DoesNotContain("jd@gmail", holder.Email);
    }

    [Fact]
    public async Task A_masked_address_keeps_the_first_character_and_the_domain_suffix()
    {
        // A mask nobody can recognise is useless in a support conversation and gets read out over the
        // telephone anyway. What must not survive is the local part.
        var masked = PiiGuard.Mask("jane.doe@example.com");

        Assert.Equal("j***@***.com", masked);
    }

    [Fact]
    public async Task A_masked_address_never_leaks_the_local_part()
    {
        var masked = PiiGuard.Mask("very.long.local.part@sub.domain.example");

        // Everything from the second character of the local part onwards is gone, and the domain keeps
        // only its public suffix, which is what makes the mask recognisable to the holder.
        Assert.Equal("v***@***.example", masked);
        Assert.DoesNotContain("local", masked);
        Assert.DoesNotContain("sub", masked);
    }

    [Fact]
    public async Task The_same_address_encrypts_differently_each_time()
    {
        var cipher = PiiCipher.Current!;

        // A fresh nonce per encryption. If the ciphertext were deterministic, the unique index on the
        // address column would still fire and two holders could be told apart by ciphertext alone.
        Assert.NotEqual(cipher.Encrypt("same@example.com"), cipher.Encrypt("same@example.com"));
    }

    [Fact]
    public async Task The_blind_index_is_the_same_for_the_same_address()
    {
        var cipher = PiiCipher.Current!;

        // The index is what makes the duplicate check possible at all, so it has to be deterministic
        // even though the ciphertext deliberately is not.
        Assert.Equal(cipher.BlindIndex("Jane@Example.com"), cipher.BlindIndex(" jane@example.com "));
        Assert.NotEqual(cipher.BlindIndex("a@example.com"), cipher.BlindIndex("b@example.com"));
    }

    [Fact]
    public async Task A_duplicate_address_is_still_refused()
    {
        await SeedHolderAsync();

        var response = await Client.PostAsJsonAsync("/api/policyholders", DefaultHolder);

        // The unique index on the blind index is not applied until the second-phase migration, so
        // the service checks it. Without that check this 409 would silently disappear for the length
        // of the rollout.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_address_is_refused_regardless_of_casing()
    {
        await SeedHolderAsync();

        var response = await Client.PostAsJsonAsync("/api/policyholders",
            new CreatePolicyHolderDto
            {
                FirstName = "Another", LastName = "Jane", Email = DefaultHolder.Email.ToUpperInvariant()
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_read_of_the_address_is_audited()
    {
        var holderId = await SeedHolderAsync();

        await GetAsRoleAsync(holderId, PolicyRoles.Admin);

        var audit = await SingleAuditAsync(holderId);

        Assert.Equal("test-user", audit.ReadBy);
        Assert.True(audit.Disclosed);
        Assert.Contains(PolicyRoles.Admin, audit.ReadByRoles);
        Assert.Equal($"/api/policyholders/{holderId}", audit.Path);
        // Joins the access to everything else that request did.
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
    }

    [Fact]
    public async Task A_read_of_a_masked_address_is_audited_as_not_disclosed()
    {
        var holderId = await SeedHolderAsync();

        await GetAsRoleAsync(holderId, PolicyRoles.Agent);

        // "Read the record" and "read the contact details" are different acts, and a disclosure
        // review needs to know which one happened.
        var audit = await SingleAuditAsync(holderId);

        Assert.False(audit.Disclosed);
        Assert.Equal("test-user", audit.ReadBy);
    }

    [Fact]
    public async Task Listing_the_holders_audits_every_holder_on_the_page()
    {
        var first = await SeedHolderAsync();
        var second = await SeedHolderAsync(new CreatePolicyHolderDto
        {
            FirstName = "Second", LastName = "Holder", Email = "second@test"
        });

        await GetAsRoleAsync(null, PolicyRoles.Admin);

        var audits = await AuditsAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, a => a.PolicyHolderId == first);
        Assert.Contains(audits, a => a.PolicyHolderId == second);
    }

    [Fact]
    public async Task The_audit_never_copies_the_data_it_audits()
    {
        var holderId = await SeedHolderAsync();

        await GetAsRoleAsync(holderId, PolicyRoles.Admin);

        var serialised = JsonSerializer.Serialize(await AuditsAsync());

        // An access log that copies the personal data it is auditing has moved the problem rather
        // than solved it, and is one more place to redact.
        Assert.DoesNotContain(DefaultHolder.Email, serialised);
    }

    [Fact]
    public async Task The_outbox_message_does_not_carry_the_address()
    {
        // Through the API, so the outbox message is actually written. Seeding the row directly would
        // leave the table empty and the assertion below would pass for the wrong reason.
        var created = await Client.PostAsJsonAsync("/api/policyholders", DefaultHolder);
        created.EnsureSuccessStatusCode();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var messages = await context.OutboxMessages.AsNoTracking().ToListAsync();
        var serialised = JsonSerializer.Serialize(messages);

        // An outbox message is a broadcast to whatever consumes it, and its retention is not this
        // service's to bound. Copying the address there would move personal data out of the one
        // store that protects it.
        Assert.DoesNotContain(DefaultHolder.Email, serialised);
        Assert.Contains("PolicyHolderCreated", serialised);
    }

    [Fact]
    public async Task A_tampered_ciphertext_fails_to_decrypt()
    {
        var cipher = PiiCipher.Current!;
        var payload = Convert.FromBase64String(cipher.Encrypt("jane@example.com"));

        // Flip a bit in the ciphertext body. GCM is authenticated, so this has to be detected rather
        // than returning corrupted personal data.
        payload[20] ^= 0x01;

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => cipher.Decrypt(Convert.ToBase64String(payload)));
    }

    [Fact]
    public void An_unusable_configuration_is_refused()
    {
        var tooShort = new PiiOptions { EncryptionKey = Convert.ToBase64String(new byte[16]), BlindIndexKey = new string('k', 40) };
        var exception = Assert.Throws<InvalidOperationException>(tooShort.Validate);

        // A 16-byte AES key is not padded up to strength. Accepting it would mean believing the data
        // is protected when it is not.
        Assert.Contains("32 bytes", exception.Message);

        var missing = new PiiOptions();
        Assert.Contains("Pii:EncryptionKey", Assert.Throws<InvalidOperationException>(missing.Validate).Message);
    }

    private async Task<PolicyHolderDto> GetAsRoleAsync(int? holderId, string role)
    {
        using var client = CreateTokenClient([role], "test-user");
        var path = holderId is null ? "/api/policyholders" : $"/api/policyholders/{holderId}";

        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();

        if (holderId is null)
        {
            var page = await response.Content.ReadFromJsonAsync<PagedResult<PolicyHolderDto>>();
            return page!.Items[0];
        }

        return (await response.Content.ReadFromJsonAsync<PolicyHolderDto>())!;
    }

    private async Task<IReadOnlyList<Models.PiiAccessAudit>> AuditsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await context.PiiAccessAudits.AsNoTracking().ToListAsync();
    }

    private async Task<Models.PiiAccessAudit> SingleAuditAsync(int policyHolderId)
    {
        var audits = await AuditsAsync();
        return Assert.Single(audits.Where(a => a.PolicyHolderId == policyHolderId));
    }
}
