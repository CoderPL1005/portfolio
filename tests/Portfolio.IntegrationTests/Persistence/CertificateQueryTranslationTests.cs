using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Features.PortfolioContent;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class CertificateQueryTranslationTests
{
    [Fact]
    public void Admin_certificate_list_shape_translates_with_the_production_npgsql_mapping()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_certificate_query_test", npgsql => npgsql.UseVector())
            .Options;
        using var context = new ApplicationDbContext(options);
        var query = context.Certificates
            .AsNoTracking()
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Id)
            .Select(item => new CertificateResult(
                item.Id, item.Name, item.Issuer, item.IssuedAt, item.ExpiresAt, item.CredentialId,
                item.CredentialUrl, item.CertificateMediaId, item.DisplayOrder, item.IsPublished));

        var sql = query.ToQueryString().Replace("\"", string.Empty, StringComparison.Ordinal);

        Assert.Contains("ORDER BY c.display_order, c.id", sql, StringComparison.OrdinalIgnoreCase);
    }
}
