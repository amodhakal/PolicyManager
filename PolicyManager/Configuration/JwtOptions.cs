using System.Text;

namespace PolicyManager.Configuration;

/// <summary>
///     Signing and validation settings for the bearer tokens the API accepts.
/// </summary>
/// <remarks>
///     Bound from the <c>Jwt</c> configuration section. Nothing here has a usable default: the
///     signing key is deliberately empty, so a deployment that has not supplied one fails at startup
///     with a message naming the setting rather than starting with a shared, guessable secret.
/// </remarks>
public class JwtOptions
{
    /// <summary>
    ///     The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "Jwt";

    /// <summary>
    ///     The expected <c>iss</c> claim. A token minted by a different issuer is rejected even if
    ///     it is signed with the right key, so a partner that shares the key cannot impersonate this
    ///     service.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    ///     The expected <c>aud</c> claim, so a token minted for another service is rejected.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    ///     The symmetric signing key.
    /// </summary>
    /// <remarks>
    ///     Supplied by configuration or an environment variable, never committed. HMAC-SHA256 needs
    ///     at least 32 bytes of key material, which <c>Validate()</c> enforces — a shorter key does
    ///     not fail loudly, it just weakens the signature.
    /// </remarks>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    ///     Tolerance for clock skew between the issuer and this service, in seconds.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    ///     Rejects a token that carries none of the roles this service knows about.
    /// </summary>
    /// <remarks>
    ///     On by default. A valid signature proves the token was minted here, not that it should be
    ///     allowed to do anything, and a token with a typo in the role claim would otherwise
    ///     authenticate successfully and then be refused by every policy — a confusing failure that
    ///     looks like a permissions bug.
    /// </remarks>
    public bool RequireKnownRole { get; set; } = true;

    /// <summary>
    ///     Whether the configuration is complete enough to validate a token.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Issuer)
        && !string.IsNullOrWhiteSpace(Audience)
        && !string.IsNullOrWhiteSpace(SigningKey);

    /// <summary>
    ///     Throws unless the configuration is complete and the key is long enough to be safe.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configuration is unusable.</exception>
    public void Validate()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(Issuer)) missing.Add($"{SectionName}:Issuer");
        if (string.IsNullOrWhiteSpace(Audience)) missing.Add($"{SectionName}:Audience");
        if (string.IsNullOrWhiteSpace(SigningKey)) missing.Add($"{SectionName}:SigningKey");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Bearer authentication is not configured: {string.Join(", ", missing)} " +
                $"must be supplied, as the {SectionName} configuration section or the " +
                $"{SectionName.ToUpperInvariant()}__* environment variables. The API will not start " +
                "with authentication silently disabled, and will not start with a default key.");
        }

        // HMAC-SHA256 is only as strong as the key. A short one does not fail — it quietly makes
        // forged tokens cheaper, which is exactly the failure nobody notices until it matters.
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException(
                $"{SectionName}:SigningKey must be at least 32 bytes for HMAC-SHA256. The " +
                "configured value is shorter, which would weaken every signature this service " +
                "accepts rather than fail.");
        }
    }
}
