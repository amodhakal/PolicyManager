namespace PolicyManager.DTOs;

/// <summary>
///     One row of the claims-ratio report: how heavily one policyholder's claims weigh on their
///     premiums.
/// </summary>
public class HolderClaimsRatioDto
{
    /// <summary>
    ///     The unique identifier of the policyholder.
    /// </summary>
    public int PolicyHolderId { get; set; }

    /// <summary>
    ///     The full name of the policyholder.
    /// </summary>
    public string PolicyholderName { get; set; } = string.Empty;

    /// <summary>
    ///     How many policies the policyholder owns.
    /// </summary>
    public long PolicyCount { get; set; }

    /// <summary>
    ///     The premium those policies carry in total.
    /// </summary>
    public decimal TotalPremium { get; set; }

    /// <summary>
    ///     How many open claims have been filed against those policies.
    /// </summary>
    public long OpenClaimCount { get; set; }

    /// <summary>
    ///     The amount those open claims are for.
    /// </summary>
    public decimal TotalOpenClaimAmount { get; set; }

    /// <summary>
    ///     The ratio of open claim amount to premium: what the policyholder's claims weigh against
    ///     what they pay.
    /// </summary>
    /// <remarks>
    ///     Zero rather than an error when the holder's premium is zero, so the column is always a
    ///     number a chart can plot.
    /// </remarks>
    public decimal ClaimsRatio { get; set; }
}
