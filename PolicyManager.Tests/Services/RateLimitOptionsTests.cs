using PolicyManager.Configuration;

namespace PolicyManager.Tests.Services;

/// <summary>
///     Covers the rules that turn configured numbers into limits that behave sensibly.
/// </summary>
public class RateLimitOptionsTests
{
    [Fact]
    public void A_non_positive_permit_limit_means_no_limit()
    {
        Assert.Null(new RateLimitOptions { PermitLimit = 0 }.PermitLimitOrNull);
        Assert.Null(new RateLimitOptions { PermitLimit = -1 }.PermitLimitOrNull);
        Assert.Equal(5, new RateLimitOptions { PermitLimit = 5 }.PermitLimitOrNull);
    }

    [Fact]
    public void A_non_positive_window_means_no_limit()
    {
        Assert.Null(new RateLimitOptions { Window = TimeSpan.Zero }.WindowOrNull);
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            new RateLimitOptions { Window = TimeSpan.FromMinutes(1) }.WindowOrNull);
    }

    [Fact]
    public void A_non_positive_queue_limit_means_no_queue()
    {
        Assert.Null(new RateLimitOptions { QueueLimit = 0 }.QueueLimitOrNull);
        Assert.Equal(20, new RateLimitOptions { QueueLimit = 20 }.QueueLimitOrNull);
    }

    [Fact]
    public void A_non_positive_body_limit_means_unlimited()
    {
        Assert.Null(new RateLimitOptions { MaxRequestBodySizeBytes = 0 }.MaxRequestBodySizeBytesOrNull);
        Assert.Equal(
            1024L,
            new RateLimitOptions { MaxRequestBodySizeBytes = 1024 }.MaxRequestBodySizeBytesOrNull);
    }

    [Fact]
    public void A_cooldown_longer_than_the_window_is_clamped_to_it()
    {
        // A caller that waits exactly as long as Retry-After says must be let through. A cooldown
        // longer than the window would have its quota reset during the wait and then reject it
        // anyway, so the advice in the header would be wrong.
        var options = new RateLimitOptions
        {
            Window = TimeSpan.FromSeconds(30),
            Cooldown = TimeSpan.FromMinutes(5)
        };

        Assert.Equal(TimeSpan.FromSeconds(30), options.EffectiveCooldown);
    }

    [Fact]
    public void A_cooldown_within_the_window_is_kept()
    {
        var options = new RateLimitOptions
        {
            Window = TimeSpan.FromMinutes(1),
            Cooldown = TimeSpan.FromSeconds(10)
        };

        Assert.Equal(TimeSpan.FromSeconds(10), options.EffectiveCooldown);
    }
}
