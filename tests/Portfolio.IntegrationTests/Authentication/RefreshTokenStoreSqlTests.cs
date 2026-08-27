using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Portfolio.Infrastructure.Authentication;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.Authentication;

public sealed class RefreshTokenStoreSqlTests
{
    [Fact]
    public void Atomic_claim_query_requires_matching_active_unexpired_token()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portfolio_auth_sql_test", npgsql => npgsql.UseVector())
            .Options;
        using var context = new ApplicationDbContext(options);
        var query = RefreshTokenStore.ActiveClaimQuery(
            context.AdminRefreshTokens,
            Guid.Parse("c9ea5499-65cf-4077-88f7-b527ba4d339f"),
            new DateTimeOffset(2026, 8, 27, 4, 0, 0, TimeSpan.Zero));

        var sql = query.ToQueryString();

        Assert.Contains("revoked_at IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires_at >", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id =", sql, StringComparison.OrdinalIgnoreCase);
    }
}
