namespace PolicyManager.Configuration;

/// <summary>
///     The API versions this service exposes.
/// </summary>
/// <remarks>
///     Named constants rather than literals scattered across the controllers, so that adding a version
///     is a single edit here and the compiler finds every place that has to be revisited.
/// </remarks>
public static class ApiVersions
{
    /// <summary>
    ///     The first version, and the one assumed when a request does not say which it wants.
    /// </summary>
    /// <remarks>
    ///     Every route that existed before versioning was introduced is a 1.0 route, so this is what
    ///     an unversioned URL means. It stays the assumed default for as long as 1.0 is supported,
    ///     which means a client that never sends a version keeps working.
    /// </remarks>
    public const string V1 = "1.0";
}
