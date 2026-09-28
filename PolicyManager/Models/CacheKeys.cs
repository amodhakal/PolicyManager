namespace PolicyManager.Models;

/// <summary>
///     Central definition of the <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache" /> keys used by the
///     application, so cache keys are never hand-built as magic strings at individual call sites.
/// </summary>
/// <remarks>
///     The only keys are per-identifier lookups. The former "all policyholders" collection key is gone:
///     the list endpoint is paginated and ordered per request, so there is no single result to cache.
///     See <c>PolicyHoldersService.GetAll</c> for the reasoning.
/// </remarks>
public static class CacheKeys
{
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
