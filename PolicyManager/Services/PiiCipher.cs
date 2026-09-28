using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PolicyManager.Configuration;

namespace PolicyManager.Services;

/// <summary>
///     Encrypts and blind-indexes personally identifiable information.
/// </summary>
/// <remarks>
///     <para>
///     Two derived values are produced from one input, because one is not enough. The ciphertext
///     protects the value: it is authenticated (AES-GCM), so a tampered row fails to decrypt instead
///     of returning corrupted personal data, and it is randomised, so two holders with the same
///     address do not produce the same bytes. The blind index is what makes a duplicate-email check
///     possible at all — ciphertext is non-deterministic, so the unique index the database enforces
///     has to be over a keyed hash rather than over the value.
///     </para>
///     <para>
///     The blind index is keyed with HMAC rather than being a plain SHA-256 of the address. An
///     unkeyed hash of an email is trivially reversible by brute force, because email addresses are
///     low-entropy and a wordlist of them is not hard to obtain. HMAC with a key the database never
///     sees makes the index safe to store in the same table as the ciphertext.
///     </para>
/// </remarks>
public sealed class PiiCipher(IOptions<PiiOptions> options)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>
    ///     A 32-byte AES key used only by the test host. Not a secret: it is in the repository, it
    ///     protects nothing, and it exists so the tests can read what the application encrypted.
    /// </summary>
    public const string EncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>
    ///     A blind-index key used only by the test host. Not a secret, for the same reason.
    /// </summary>
    public const string BlindIndexKey = "test-blind-index-key-that-is-long-enough-for-hmac";

    private readonly byte[] _key = Convert.FromBase64String(options.Value.EncryptionKey);
    private readonly byte[] _indexKey = Encoding.UTF8.GetBytes(options.Value.BlindIndexKey);

    /// <summary>
    ///     The instance the EF value converters use.
    /// </summary>
    /// <remarks>
    ///     Static because an EF <c>ValueConverter</c> is built while the model is being constructed,
    ///     which happens before any service provider exists to resolve <see cref="IOptions{TOptions}" />
    ///     from. It is assigned once at startup, before the first context is created, and the
    ///     expression is captured rather than re-read per row so a conversion does not cost a
    ///     service resolution.
    /// </remarks>
    public static PiiCipher? Current { get; private set; }

    /// <summary>
    ///     Publishes the instance the value converters will use.
    /// </summary>
    /// <param name="cipher">The configured cipher.</param>
    public static void Use(PiiCipher cipher) => Current = cipher;

    /// <summary>
    ///     Encrypts a value for storage.
    /// </summary>
    /// <param name="plaintext">The value to protect.</param>
    /// <returns>
    ///     Base64 of nonce, ciphertext and authentication tag. A fresh nonce per call, so the same
    ///     input never produces the same output.
    /// </returns>
    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[NonceSize + cipher.Length + TagSize];
        nonce.CopyTo(payload, 0);
        cipher.CopyTo(payload, NonceSize);
        tag.CopyTo(payload, NonceSize + cipher.Length);

        return Convert.ToBase64String(payload);
    }

    /// <summary>
    ///     Decrypts a stored value.
    /// </summary>
    /// <param name="ciphertext">The value as stored.</param>
    /// <returns>The original value.</returns>
    /// <exception cref="CryptographicException">
    ///     The value is not readable with the configured key, or has been tampered with.
    /// </exception>
    public string Decrypt(string ciphertext)
    {
        var payload = Convert.FromBase64String(ciphertext);

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException(
                "The stored value is too short to be a valid ciphertext. It is either truncated or " +
                "was never encrypted.");

        var nonce = payload[..NonceSize];
        var tag = payload[^TagSize..];
        var cipher = payload[NonceSize..^TagSize];
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    ///     Produces the keyed hash a unique index can be built on.
    /// </summary>
    /// <param name="value">The value to index.</param>
    /// <returns>Base64 of the HMAC.</returns>
    public string BlindIndex(string value)
        => Convert.ToBase64String(HMACSHA256.HashData(_indexKey, Encoding.UTF8.GetBytes(Normalise(value))));

    /// <summary>
    ///     Reduces a value to a form that is insensitive to how it was typed.
    /// </summary>
    /// <remarks>
    ///     Lower-cased and trimmed only. Addresses are not case-folded beyond this in general, and
    ///     normalising more aggressively would merge distinct local parts that some providers treat
    ///     as different — turning a legitimate holder into a duplicate.
    /// </remarks>
    /// <param name="value">The value to normalise.</param>
    /// <returns>The normalised value.</returns>
    public static string Normalise(string value) => value.Trim().ToLowerInvariant();
}
