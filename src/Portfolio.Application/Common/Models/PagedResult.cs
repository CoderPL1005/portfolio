namespace Portfolio.Application.Common.Models;

public sealed class PagedResult<T>
{
    public PagedResult(IEnumerable<T> items, int page, int pageSize, int total)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(total);

        Items = items.ToArray();
        Page = page;
        PageSize = pageSize;
        Total = total;
        TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
    }

    public IReadOnlyList<T> Items { get; }
    public int Page { get; }
    public int PageSize { get; }
    public int Total { get; }
    public int TotalPages { get; }
}
