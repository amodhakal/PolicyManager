using System.Text.Json.Serialization;

namespace PolicyManager.Models.Enums;

/// <summary>
///     Represents the status of an insurance policy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PolicyStatus
{
    /// <summary>
    ///     The policy is currently active.
    /// </summary>
    Active,

    /// <summary>
    ///     The policy has been canceled.
    /// </summary>
    Cancelled,

    /// <summary>
    ///     The policy has expired.
    /// </summary>
    Expired
}