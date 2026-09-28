namespace PolicyManager.DTOs;

/// <summary>
///     The paging and sorting options bound from a list endpoint's query string.
/// </summary>
/// <remarks>
///     Every property has a default, so a bare <c>GET /api/policies</c> is a valid request and
///     returns the first page sorted by the service's documented default. The values are
///     unvalidated when bound — <c>?pageSize=-1</c> and <c>?pageSize=100000</c> both arrive here
///     intact — so the service calls <see cref="Normalize" /> before using them. Clamping beats
///     rejecting: a caller with a bad page size wants the nearest sensible page, not a 400.
/// </remarks>
public class PaginationQuery
{
    /// <summary>
    ///     The largest page a client may ask for, whatever it requests.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    ///     The page size used when the caller does not supply one.
    /// </summary>
    public const int DefaultPageSize = 25;

    /// <summary>
    ///     The one-based page number to return. Values below 1 are clamped to 1 by
    ///     <see cref="Normalize" />.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    ///     The number of items a page may carry. Clamped to between 1 and <see cref="MaxPageSize" />
    ///     by <see cref="Normalize" />.
    /// </summary>
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    ///     The name of the field to sort by, or null to use the service's documented default. An
    ///     unrecognised name falls back to that default rather than failing the request, so a
    ///     client that guesses wrong still gets data instead of an error.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    ///     Whether to reverse the order established by <see cref="SortBy" />.
    /// </summary>
    public bool Descending { get; set; }

    /// <summary>
    ///     Brings the requested page and page size into the range this API supports: the page to at
    ///     least 1, and the page size to 1..<see cref="MaxPageSize" />.
    /// </summary>
    public void Normalize()
    {
        Page = Math.Max(Page, 1);
        PageSize = Math.Clamp(PageSize, 1, MaxPageSize);
    }
}
