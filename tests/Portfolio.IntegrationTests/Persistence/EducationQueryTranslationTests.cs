using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class EducationQueryTranslationTests
{
    [Fact]
    public void Admin_education_list_shape_translates_with_the_production_npgsql_mapping()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_education_query_test", npgsql => npgsql.UseVector())
            .Options;
        using var context = new ApplicationDbContext(options);
        var query = context.Educations
            .AsNoTracking()
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Id)
            .Select(item => new EducationResult(
                item.Id, item.Institution, item.Degree, item.FieldOfStudy, item.StartDate,
                item.EndDate, item.Description, item.Location, item.DisplayOrder, item.IsPublished));

        var sql = query.ToQueryString().Replace("\"", string.Empty, StringComparison.Ordinal);

        Assert.Contains("ORDER BY e.display_order, e.id", sql, StringComparison.OrdinalIgnoreCase);
    }
}
