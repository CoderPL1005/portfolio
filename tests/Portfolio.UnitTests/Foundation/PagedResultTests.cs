using Portfolio.Application.Common.Models;

namespace Portfolio.UnitTests.Foundation;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    public void Constructor_calculates_total_pages(int total, int pageSize, int expectedTotalPages)
    {
        var result = new PagedResult<int>([1, 2], page: 1, pageSize, total);

        Assert.Equal([1, 2], result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(pageSize, result.PageSize);
        Assert.Equal(total, result.Total);
        Assert.Equal(expectedTotalPages, result.TotalPages);
    }

    [Fact]
    public void Constructor_rejects_invalid_pagination_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PagedResult<int>([], page: 0, pageSize: 20, total: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PagedResult<int>([], page: 1, pageSize: 0, total: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PagedResult<int>([], page: 1, pageSize: 20, total: -1));
    }
}
