using System.Text.Json.Serialization;

namespace PolicyManager.Models.Enums;

/// <summary>
///     Represents the type of insurance policy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PolicyType
{
    /// <summary>
    ///     Auto insurance policy.
    /// </summary>
    Auto,

    /// <summary>
    ///     Home insurance policy.
    /// </summary>
    Home,

    /// <summary>
    ///     Life insurance policy.
    /// </summary>
    Life
}