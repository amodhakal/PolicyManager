namespace PolicyManager.Models;

/// <summary>
///     Central definition of the <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache" /> keys used by the
///     application, so cache keys are never hand-built as magic strings at individual call sites.
/// </summary>
/// <remarks>
///     The only keys are per-identifier lookups. The former "all policyholders" collection key is gone:
///     the list endpoint is paginated and ordered per request, so there is no single result to cache.
///     See <c>PolicyHoldersService.GetAll</c> for the reasoning.
///     <para>
///         The generation of the holder is part of the key, and that is what makes a write safe rather
///         than merely fast. See <see cref="PolicyHolderWriteGenerations" /> for why evicting the key
///         is not enough on its own.
///     </para>
/// </remarks>
public static class CacheKeys
{
    /// <summary>
    ///     Builds the cache key for a single policyholder in a given write generation.
    /// </summary>
    /// <param name="id">The policyholder identifier used to build the key.</param>
    /// <param name="generation">
    ///     The holder's current write generation, from
    ///     <see cref="PolicyHolderWriteGenerations.Current" />. Any write to the holder retires this
    ///     key for good, which is what stops a read that was already in flight from resurrecting the
    ///     value it read before the write.
    /// </param>
    /// <returns>The cache key for the policyholder with the specified identifier and generation.</returns>
    public static string ById(int id, long generation)
    {
        return $"policyholders:{id}:g{generation}";
    }
}
