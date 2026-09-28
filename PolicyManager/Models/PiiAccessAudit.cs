using System.ComponentModel.DataAnnotations;

namespace PolicyManager.Models;

/// <summary>
///     A record that a policyholder's personal data was read, and by whom.
/// </summary>
/// <remarks>
///     The access log for personal data is the thing an auditor asks for first, and the thing that
///     does not exist by default. It records <em>that</em> the data was seen, never the data itself:
///     an audit log that copies the personal data it is auditing has moved the problem rather than
///     solved it, and is one more place to redact.
/// </remarks>
public class PiiAccessAudit
{
    /// <summary>
    ///     The surrogate identifier of the audit row.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    ///     Whose personal data was read.
    /// </summary>
    public int PolicyHolderId { get; set; }

    /// <summary>
    ///     Who read it — the authenticated subject, or the fixed system token for an unattributed read.
    /// </summary>
    [MaxLength(100)]
    public string? ReadBy { get; set; }

    /// <summary>
    ///     The roles the reader held, so the log says what they were permitted to do, not just who
    ///     they were.
    /// </summary>
    [MaxLength(200)]
    public string? ReadByRoles { get; set; }

    /// <summary>
    ///     When the read happened, in UTC.
    /// </summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>
    ///     The correlation ID of the request, so the access can be joined to the rest of what that
    ///     request did.
    /// </summary>
    [MaxLength(128)]
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     The path that returned the personal data.
    /// </summary>
    [MaxLength(256)]
    public string? Path { get; set; }

    /// <summary>
    ///     Whether the value returned was the address itself or a masked form of it.
    /// </summary>
    /// <remarks>
    ///     Recorded separately because "read the record" and "read the contact details" are different
    ///     acts, and a disclosure review needs to know which one happened.
    /// </remarks>
    public bool Disclosed { get; set; }
}
