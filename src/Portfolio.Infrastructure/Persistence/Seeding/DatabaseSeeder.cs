namespace Portfolio.Infrastructure.Persistence.Seeding;

public sealed class DatabaseSeeder(PortfolioSeeder portfolioSeeder, AdminSeeder adminSeeder)
{
    public async Task SeedAsync(string portfolioSeedPath, CancellationToken cancellationToken = default)
    {
        await portfolioSeeder.SeedAsync(portfolioSeedPath, cancellationToken);
        await adminSeeder.SeedAsync(cancellationToken);
    }
}
