using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;
using PolicyManager.Data;
using PolicyManager.Middleware;
using PolicyManager.Models;

namespace PolicyManager.Services;

/// <summary>
///     Applies the access control and writes the audit trail for personal data.
/// </summary>
public interface IPiiGuard
{
    /// <summary>
    ///     Decides whether the current caller may see a policyholder's email address in the clear.
    /// </summary>
    /// <returns>True when the address itself may be returned; false when it must be masked.</returns>
    bool MayDisclose();

    /// <summary>
    ///     Records that a policyholder's personal data was read.
    /// </summary>
    /// <param name="policyHolderId">Whose data was read.</param>
    /// <param name="disclosed">Whether the address itself was returned, or a masked form of it.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    Task RecordAccessAsync(
        int policyHolderId, bool disclosed, CancellationToken cancellationToken = default);
}

/// <summary>
///     Restricts disclosure of policyholder contact details, and records every read of them.
/// </summary>
/// <remarks>
///     <para>
///     Authorization already stops an unauthenticated caller and confines writes by role. What it
///     does not do is narrow <em>reads</em>: an agent who may file a claim has no need for a
///     policyholder's email address, and returning it anyway means a breach of an agent account
///     discloses contact details for the whole book. The agent therefore gets the record with a
///     masked address, and the read is still recorded either way — "read the record" and "read the
///     contact details" are different acts, and a disclosure review needs to know which happened.
///     </para>
///     <para>
///     The audit row is written in the request's own context, so it commits with the read that caused
///     it. Writing it afterwards would leave a window where a successful disclosure had no record at
///     all, which is exactly the window an auditor would ask about.
///     </para>
/// </remarks>
public class PiiGuard(
    AppDbContext context,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor,
    IOptions<PiiOptions> options,
    ILogger<PiiGuard> logger) : IPiiGuard
{
    /// <summary>
    ///     The roles permitted to see an address in the clear. An adjuster contacts holders about
    ///     claims, so they need it; an agent files on their behalf and does not.
    /// </summary>
    private static readonly string[] DisclosiveRoles = [PolicyRoles.Admin, PolicyRoles.Adjuster];

    /// <inheritdoc />
    public bool MayDisclose() => DisclosiveRoles.Any(currentUser.IsInRole);

    /// <inheritdoc />
    public async Task RecordAccessAsync(
        int policyHolderId, bool disclosed, CancellationToken cancellationToken = default)
    {
        if (!options.Value.AuditAccess)
        {
            // Not silent, though. A switched-off access log is a decision someone made, and the
            // decision belongs in the log itself.
            logger.LogWarning(
                "PII access auditing is disabled. Read of holder {PolicyHolderId} by {Actor} was not recorded.",
                policyHolderId,
                currentUser.Actor);
            return;
        }

        var httpContext = httpContextAccessor.HttpContext;

        context.PiiAccessAudits.Add(new PiiAccessAudit
        {
            PolicyHolderId = policyHolderId,
            ReadBy = currentUser.Actor,
            ReadByRoles = currentUser.Roles.Count == 0 ? null : string.Join(",", currentUser.Roles),
            OccurredAt = DateTime.UtcNow,
            CorrelationId = httpContext?.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var id) == true
                ? id as string
                : httpContext?.TraceIdentifier,
            Path = httpContext?.Request.Path.Value,
            Disclosed = disclosed
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    ///     Reduces an address to a form that is recognisable to its owner and useless to anyone else.
    /// </summary>
    /// <remarks>
    ///     The first character of the local part and the domain's last label survive, because a masked
    ///     value nobody can recognise is useless in a support conversation and gets read out over the
    ///     telephone anyway. The length is not derived from the original, so it leaks nothing about
    ///     it.
    /// </remarks>
    /// <param name="email">The address to mask.</param>
    /// <returns>The masked form.</returns>
    public static string Mask(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;

        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "***";

        var local = email[..at];
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');

        var maskedDomain = dot > 0 ? $"***{domain[dot..]}" : "***";

        return $"{local[0]}***@{maskedDomain}";
    }
}
