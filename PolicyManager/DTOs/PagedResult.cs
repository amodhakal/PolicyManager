namespace PolicyManager.DTOs;

/// <summary>
///     Envelope wrapping a single page of results together with the metadata a client needs to ask
///     for the next one.
/// </summary>
/// <typeparam name="T">The element type of the page.</typeparam>
/// <remarks>
///     List endpoints return this instead of a bare array so the caller learns the size of the whole
///     result set without a second request. A page is a snapshot: the count describes the rows that
///     matched when the page was read, and rows written afterwards are not reflected in it.
/// </remarks>
public class PagedResult<T>
{
    /// <summary>
    ///     The items on the requested page, empty when the page is past the end of the result set.
    /// </summary>
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    /// <summary>
    ///     The one-based page number that was returned. Never below 1.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    ///     The maximum number of items a page can carry. Always between 1 and
    ///     <see cref="PaginationQuery.MaxPageSize" />.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    ///     The total number of items matching the request across every page, ignoring
    ///     <see cref="Page" /> and <see cref="PageSize" />.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    ///     The number of pages the result set spans, or 0 when there is nothing to page through.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    ///     Whether a page exists before this one.
    /// </summary>
    public bool HasPrevious { get; set; }

    /// <summary>
    ///     Whether a page exists after this one.
    /// </summary>
    public bool HasNext { get; set; }

    /// <summary>
    ///     Builds a page envelope, deriving <see cref="TotalPages" />, <see cref="HasPrevious" /> and
    ///     <see cref="HasNext" /> so no caller can publish inconsistent navigation metadata.
    /// </summary>
    /// <param name="items">The items on the requested page.</param>
    /// <param name="totalCount">The total number of matching items across all pages.</param>
    /// <param name="page">The one-based page number that was returned; values below 1 are treated as 1.</param>
    /// <param name="pageSize">
    ///     The page size that was applied; values below 1 are treated as 1 so the page-count
    ///     division cannot divide by zero.
    /// </param>
    /// <returns>The page envelope for the supplied page.</returns>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);

        var safePageSize = pageSize < 1 ? 1 : pageSize;
        var safePage = page < 1 ? 1 : page;
        var safeTotalCount = totalCount < 0 ? 0 : totalCount;

        // An empty result set has no pages at all, and HasNext is then false on every page. The
        // division is promoted to double so a large count divided by a large page size does not
        // overflow to a negative page count.
        var totalPages = safeTotalCount == 0
            ? 0
            : (int)Math.Ceiling(safeTotalCount / (double)safePageSize);

        return new PagedResult<T>
        {
            Items = items,
            Page = safePage,
            PageSize = safePageSize,
            TotalCount = safeTotalCount,
            TotalPages = totalPages,
            HasPrevious = safePage > 1,
            HasNext = safePage < totalPages
        };
    }
}
