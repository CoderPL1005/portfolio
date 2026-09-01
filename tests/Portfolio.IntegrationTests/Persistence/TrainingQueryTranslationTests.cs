using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class TrainingQueryTranslationTests
{
    [Fact]
    public void Admin_training_list_shape_translates_with_the_production_npgsql_mapping()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_training_query_test", npgsql => npgsql.UseVector())
            .Options;
        using var context = new ApplicationDbContext(options);
        var query = context.Trainings
            .AsNoTracking()
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Id)
            .Select(item => new TrainingResult(
                item.Id, item.Title, item.Provider, item.Description, item.StartDate, item.EndDate,
                item.CredentialUrl, item.DisplayOrder, item.IsPublished));

        var sql = query.ToQueryString().Replace("\"", string.Empty, StringComparison.Ordinal);

        Assert.Contains("ORDER BY t.display_order, t.id", sql, StringComparison.OrdinalIgnoreCase);
    }
}
