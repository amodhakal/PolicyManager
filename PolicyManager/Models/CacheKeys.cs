namespace PolicyManager.Models;

/// <summary>
///     Central definition of the <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache" /> keys used by the
///     application, so cache keys are never hand-built as magic strings at individual call sites.
/// </summary>
public static class CacheKeys
{
    /// <summary>
    ///     The key under which the cached collection of all policyholders is stored.
    /// </summary>
    public const string AllPolicyHolders = "policyholders:all";

    /// <summary>
    ///     Builds the cache key for a single policyholder identified by its unique identifier.
    /// </summary>
    /// <param name="id">The policyholder identifier used to build the key.</param>
    /// <returns>The cache key for the policyholder with the specified identifier.</returns>
    public static string ById(int id)
    {
        return $"policyholders:{id}";
    }
}
