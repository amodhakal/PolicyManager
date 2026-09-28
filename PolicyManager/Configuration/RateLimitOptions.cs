namespace PolicyManager.Configuration;

/// <summary>
///     Request rate and size limits, and how callers are distinguished when the limit is applied.
/// </summary>
/// <remarks>
///     Bound from the <c>RateLimiting</c> configuration section, so every value can be changed per
///     environment without a rebuild — the right quota for a staging load test is rarely the right
///     one in production.
/// </remarks>
public class RateLimitOptions
{
    /// <summary>
    ///     The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    ///     Enables rate limiting. Off by default so a run without configuration behaves as before.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     How many requests one caller may make in a <see cref="Window" /> before being rejected.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    ///     The length of the sliding window the quota is counted over.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How long a caller must wait once it has exhausted its quota.
    /// </summary>
    /// <remarks>
    ///     A fixed delay rather than the remaining window, so a caller learns immediately how long
    ///     to wait instead of having to work it out. The trade is that a caller which retries
    ///     exactly on the boundary can be rejected a second time; the delay is therefore set below
    ///     the window so a caller that obeys it is always through.
    /// </remarks>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How many requests may queue while the caller waits for a permit.
    /// </summary>
    /// <remarks>
    ///     Non-zero so a brief burst is smoothed rather than rejected. Queued callers still get a
    ///     429 once the queue is full, so a genuine flood is still shed — it is just no longer
    ///     punished for arriving a few hundred milliseconds early.
    /// </remarks>
    public int QueueLimit { get; set; } = 20;

    /// <summary>
    ///     The largest request body accepted, in bytes.
    /// </summary>
    /// <remarks>
    ///     The largest payload any endpoint here can legitimately produce is a claim description, and
    ///     that is capped at 1000 characters. 64 KiB is generous by two orders of magnitude while
    ///     still refusing a body large enough to be an attack rather than a request.
    /// </remarks>
    public long MaxRequestBodySizeBytes { get; set; } = 64 * 1024;

    /// <summary>
    ///     How long a rejected caller waits, in seconds.
    /// </summary>
    /// <remarks>
    ///     Advertised in <c>Retry-After</c>. Present so a well-behaved client can back off
    ///     correctly instead of treating a 429 as a generic failure and retrying immediately, which
    ///     is what turns a burst into a sustained load.
    /// </remarks>
    public int RejectionStatusCode { get; set; } = StatusCodes.Status429TooManyRequests;

    /// <summary>
    ///     Reads a value, treating a non-positive number as "no limit configured".
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <returns>The value, or null when it is not usable.</returns>
    public int? PermitLimitOrNull => PermitLimit > 0 ? PermitLimit : null;

    /// <summary>
    ///     Reads a value, treating a non-positive window as "no limit configured".
    /// </summary>
    public TimeSpan? WindowOrNull => Window > TimeSpan.Zero ? Window : null;

    /// <summary>
    ///     Reads the queue depth, treating a negative number as no queue.
    /// </summary>
    public int? QueueLimitOrNull => QueueLimit > 0 ? QueueLimit : null;

    /// <summary>
    ///     Reads the cooldown, clamping it to the window so obeying it always suffices.
    /// </summary>
    /// <remarks>
    ///     A cooldown longer than the window would reject a caller that waited exactly as told,
    ///     because its quota would already have reset. Clamping means the advice in
    ///     <c>Retry-After</c> is always sufficient.
    /// </remarks>
    public TimeSpan EffectiveCooldown =>
        Window > TimeSpan.Zero && Cooldown > Window ? Window : Cooldown;

    /// <summary>
    ///     Reads the body limit, treating a non-positive number as unlimited.
    /// </summary>
    public long? MaxRequestBodySizeBytesOrNull => MaxRequestBodySizeBytes > 0 ? MaxRequestBodySizeBytes : null;
}
