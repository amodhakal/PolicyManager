using System.Text.Json.Serialization;

namespace PolicyManager.Models.Enums;

/// <summary>
///     Represents the status of an insurance claim.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClaimStatus
{
    /// <summary>
    ///     The claim is pending review.
    /// </summary>
    Pending,

    /// <summary>
    ///     The claim has been approved.
    /// </summary>
    Approved,

    /// <summary>
    ///     The claim has been denied.
    /// </summary>
    Denied
}