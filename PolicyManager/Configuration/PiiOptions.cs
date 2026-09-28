using System.Text;

namespace PolicyManager.Configuration;

/// <summary>
///     How personally identifiable information is protected at rest.
/// </summary>
/// <remarks>
///     Bound from the <c>Pii</c> configuration section. The key has no default and no fallback:
///     <see cref="Validate" /> refuses to start without one, because "encryption" that silently
///     degrades to plaintext is worse than no encryption at all — it is encryption on a dashboard
///     nobody reads.
/// </remarks>
public class PiiOptions
{
    /// <summary>
    ///     The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "Pii";

    /// <summary>
    ///     The AES-256-GCM key, as base64, protecting PII at rest.
    /// </summary>
    /// <remarks>
    ///     Never committed. Supplied as <c>Pii__EncryptionKey</c> from a secret store. Rotating it
    ///     means re-encrypting every row, which is why it is a single key rather than a per-column
    ///     scheme that would need version tracking to stay readable.
    /// </remarks>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    ///     Key derivation label mixed into the blind index, so the same email cannot be correlated
    ///     across two databases that happen to share a key.
    /// </summary>
    public string BlindIndexKey { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the audit row for every PII read is written.
    /// </summary>
    /// <remarks>
    ///     On by default and not configurable to off in any environment a regulated deployment would
    ///     recognise. It exists as a switch so an emergency can be turned off deliberately, and
    ///     visibly, rather than by editing code.
    /// </remarks>
    public bool AuditAccess { get; set; } = true;

    /// <summary>
    ///     Whether the one-off conversion of pre-existing addresses runs on start-up.
    /// </summary>
    /// <remarks>
    ///     On by default, and only does anything on a database that still holds addresses written
    ///     before encryption existed: it selects rows with no blind index, so a converted table makes
    ///     it exit on its first pass. It is a switch so an operator can turn it off deliberately, and
    ///     visibly, rather than by editing code.
    /// </remarks>
    public bool BackfillOnStartup { get; set; } = true;

    /// <summary>
    ///     Whether the configuration is complete enough to protect anything.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(EncryptionKey) && !string.IsNullOrWhiteSpace(BlindIndexKey);

    /// <summary>
    ///     Throws unless the configuration is complete and the keys are the right length.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configuration is unusable.</exception>
    public void Validate()
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(EncryptionKey)) missing.Add($"{SectionName}:EncryptionKey");
        if (string.IsNullOrWhiteSpace(BlindIndexKey)) missing.Add($"{SectionName}:BlindIndexKey");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"PII protection is not configured: {string.Join(", ", missing)} must be " +
                $"supplied, as the {SectionName} configuration section or the " +
                $"{SectionName.ToUpperInvariant()}__* environment variables. The API will not start " +
                "with personal data stored in the clear, and will not start with a default key.");
        }

        // AES-256 takes exactly 32 bytes. A shorter key is not padded up to strength, so accepting one
        // would mean believing the data is protected when it is not.
        if (Convert.FromBase64String(EncryptionKey).Length != 32)
        {
            throw new InvalidOperationException(
                $"{SectionName}:EncryptionKey must decode to exactly 32 bytes for AES-256-GCM. " +
                "Generate one with: openssl rand -base64 32");
        }

        if (Encoding.UTF8.GetByteCount(BlindIndexKey) < 32)
        {
            throw new InvalidOperationException(
                $"{SectionName}:BlindIndexKey must be at least 32 bytes. A shorter one would make " +
                "the blind index guessable by brute force, which is a slow way to enumerate every " +
                "email address in the table.");
        }
    }
}
