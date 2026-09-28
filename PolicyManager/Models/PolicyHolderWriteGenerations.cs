using System.Collections.Concurrent;

namespace PolicyManager.Models;

/// <summary>
///     Holds the write generation of each policyholder, which is what makes a cached read safe
///     against a concurrent write.
/// </summary>
/// <remarks>
///     <para>
///         Removing a cache key on write is not enough. A read that started before the write and
///         finishes after it has already read the old row from the database, and populating the key
///         it removed puts that old row back: the write happened, the eviction happened, and the
///         stale value is then served for the whole cache lifetime. The window is one database round
///         trip wide and grows under exactly the load that produces the most writes.
///     </para>
///     <para>
///         The generation is therefore part of the key rather than something evicted. Every write
///         bumps the generation after its transaction commits, which retires every key built from
///         the previous generation at once. A read that finishes late writes to a key no reader will
///         ask for again, so it is wasted memory rather than a stale answer, and a read that starts
///         after the bump reads a generation whose commit is already visible.
///     </para>
///     <para>
///         The counter lives here rather than in <c>IMemoryCache</c> because it must never be
///         evicted. An evicted generation would restart at zero and make a long-dead key reachable
///         again, reintroducing the exact staleness this type exists to prevent.
///     </para>
/// </remarks>
public sealed class PolicyHolderWriteGenerations
{
    private readonly ConcurrentDictionary<int, long> _generations = new();

    /// <summary>
    ///     Gets the current write generation for a policyholder.
    /// </summary>
    /// <param name="id">The policyholder identifier.</param>
    /// <returns>
    ///     The generation to build a cache key from. Zero for a holder that has never been written.
    /// </returns>
    public long Current(int id)
    {
        return _generations.TryGetValue(id, out var generation) ? Volatile.Read(ref generation) : 0L;
    }

    /// <summary>
    ///     Retires every cache key built from a policyholder's current generation.
    /// </summary>
    /// <remarks>
    ///     Called after the write has committed, never before. A reader that sees the new generation
    ///     is therefore guaranteed to see the committed row too: anything it reads afterwards comes
    ///     from a database that already answers with the new value.
    /// </remarks>
    /// <param name="id">The policyholder identifier that was written.</param>
    /// <returns>The new generation.</returns>
    public long Advance(int id)
    {
        // AddOrUpdate rather than reading, incrementing and writing back: under contention the
        // delegate can be invoked more than once, so a read-modify-write across separate calls could
        // lose a writer's bump and hand two different writes the same generation - retiring the same
        // keys twice and leaving a reader's key reachable after the second write. AddOrUpdate
        // applies exactly one of the values it produced.
        var generation = _generations.AddOrUpdate(id, 1L, (_, current) => current + 1);

        return generation;
    }
}
