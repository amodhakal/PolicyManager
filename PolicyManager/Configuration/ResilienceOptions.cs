namespace PolicyManager.Configuration;

/// <summary>
///     Retry, circuit-breaker and timeout budgets for every outbound call this application makes.
/// </summary>
/// <remarks>
///     <para>
///     Bound from the <c>Resilience</c> configuration section, so a deployment can loosen or tighten
///     the budgets per environment without a rebuild.
///     </para>
///     <para>
///     One type rather than three because the three destinations fail for the same reasons and are
///     fixed the same way — a short exponential backoff and a circuit breaker that opens while the
///     dependency is down. What differs is the ordering of the numbers, and those are separate
///     sections so a database can be treated differently from a broker without a second option type
///     doing the same job.
///     </para>
/// </remarks>
public class ResilienceOptions
{
    /// <summary>
    ///     The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "Resilience";

    /// <summary>
    ///     Budgets for outbound HTTP calls.
    /// </summary>
    public DependencyResilienceOptions Http { get; set; } = new()
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        MaxDelay = TimeSpan.FromSeconds(2),
        BreakDuration = TimeSpan.FromSeconds(10),
        FailureRatio = 0.5,
        MinimumThroughput = 10,
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    ///     Budgets for calls to SQL Server that are not part of a unit of work.
    /// </summary>
    public DependencyResilienceOptions Database { get; set; } = new()
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromMilliseconds(200),
        MaxDelay = TimeSpan.FromSeconds(5),
        BreakDuration = TimeSpan.FromSeconds(15),
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        Timeout = TimeSpan.FromSeconds(5)
    };

    /// <summary>
    ///     Budgets for publishing to the message broker.
    /// </summary>
    public DependencyResilienceOptions Broker { get; set; } = new()
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromMilliseconds(200),
        MaxDelay = TimeSpan.FromSeconds(5),
        BreakDuration = TimeSpan.FromSeconds(30),
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    ///     Throws when a budget is set to a value that would silently disable the protection it
    ///     describes.
    /// </summary>
    /// <exception cref="InvalidOperationException">A budget is out of range.</exception>
    public void Validate()
    {
        var errors = new List<string>();

        Collect(Http, nameof(Http), errors);
        Collect(Database, nameof(Database), errors);
        Collect(Broker, nameof(Broker), errors);

        if (errors.Count == 0) return;

        throw new InvalidOperationException(
            $"Resilience budgets are invalid: {string.Join(" ", errors)}");
    }

    private static void Collect(
        DependencyResilienceOptions options,
        string name,
        ICollection<string> errors)
    {
        var prefix = $"{SectionName}:{name}";

        if (options.MaxRetryAttempts is < 0 or > 10)
            errors.Add($"{prefix}:MaxRetryAttempts must be between 0 and 10 but was {options.MaxRetryAttempts}. ");

        if (options.Delay <= TimeSpan.Zero)
            errors.Add($"{prefix}:Delay must be positive. ");

        if (options.MaxDelay < options.Delay)
            errors.Add($"{prefix}:MaxDelay must be at least Delay. ");

        if (options.BreakDuration <= TimeSpan.Zero)
            errors.Add($"{prefix}:BreakDuration must be positive. ");

        if (options.Timeout <= TimeSpan.Zero)
            errors.Add($"{prefix}:Timeout must be positive. ");

        if (options.FailureRatio is <= 0 or > 1)
            errors.Add($"{prefix}:FailureRatio must be between 0 and 1 exclusive. ");

        if (options.MinimumThroughput < 2)
            errors.Add($"{prefix}:MinimumThroughput must be at least 2, or the breaker opens on a single call. ");
    }
}

/// <summary>
///     The budgets applied to one outbound dependency.
/// </summary>
public class DependencyResilienceOptions
{
    /// <summary>
    ///     How many times a failed call is retried before the failure is surfaced.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>
    ///     The first backoff delay. Each retry doubles it, up to <see cref="MaxDelay" />.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    ///     The ceiling for the doubling, so a deep retry chain cannot wait for minutes.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Jitter applied to the backoff.
    /// </summary>
    /// <remarks>
    ///     On by default because without it every caller that failed at the same instant retries at
    ///     the same instant, which is how a recovering dependency gets knocked over again.
    /// </remarks>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    ///     How long the circuit stays open before a probe is allowed through.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The share of failures over the sampling window at which the circuit opens.
    /// </summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>
    ///     How many calls must complete within the sampling window before the failure ratio is
    ///     considered at all.
    /// </summary>
    /// <remarks>
    ///     The floor that stops a single unlucky call from opening the circuit. The default is ten
    ///     because a dependency that is only failing intermittently should not be taken out of
    ///     service on one bad request.
    /// </remarks>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>
    ///     How long a single attempt may take before it is abandoned.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}
