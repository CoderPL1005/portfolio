using Microsoft.Extensions.Configuration;
using Portfolio.Infrastructure.Persistence.Seeding;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class SeedingTests
{
    [Fact]
    public async Task Portfolio_seed_file_deserializes_and_has_unique_natural_identities()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "seed", "portfolio.seed.json");
        var seed = await PortfolioSeedData.LoadAsync(path);

        Assert.Equal("1.0", seed.SeedVersion);
        Assert.Equal(25, seed.Technologies.Count);
        Assert.Equal(3, seed.Projects.Count);
        Assert.Equal(17, seed.Skills.Count);
        Assert.Equal(3, seed.Journey.Count);
        Assert.Equal(
            ["C# / .NET Foundations", "Angular & Full-stack Development", "AI & Cloud Engineering"],
            seed.Journey.Select(item => item.Title));
        Assert.Equal(seed.Technologies.Count,
            seed.Technologies.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(seed.Projects.Count,
            seed.Projects.Select(item => item.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(seed.Skills.Count,
            seed.Skills.Select(item => PortfolioSeeder.CompositeKey(item.Name, item.Category))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Nguyễn Đình Phúc's AI representative", seed.AgentSettings.WelcomeMessage);
        Assert.Contains("retrieved portfolio context", seed.AgentSettings.SystemPrompt);
    }

    [Fact]
    public async Task Applying_seed_identity_rules_twice_produces_no_second_pass_items()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "seed", "portfolio.seed.json");
        var seed = await PortfolioSeedData.LoadAsync(path);
        var existingProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingTechnologies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(seed.Projects.Count, seed.Projects.Count(item => existingProjects.Add(item.Slug)));
        Assert.Equal(seed.Technologies.Count, seed.Technologies.Count(item => existingTechnologies.Add(item.Name)));
        Assert.Equal(seed.Skills.Count, seed.Skills.Count(item =>
            existingSkills.Add(PortfolioSeeder.CompositeKey(item.Name, item.Category))));

        Assert.Equal(0, seed.Projects.Count(item => existingProjects.Add(item.Slug)));
        Assert.Equal(0, seed.Technologies.Count(item => existingTechnologies.Add(item.Name)));
        Assert.Equal(0, seed.Skills.Count(item =>
            existingSkills.Add(PortfolioSeeder.CompositeKey(item.Name, item.Category))));
    }

    [Fact]
    public void Admin_bootstrap_is_disabled_when_credentials_are_missing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        Assert.False(AdminBootstrapCredentials.TryCreate(configuration, out _));
    }

    [Fact]
    public void Admin_bootstrap_uses_only_explicit_configuration()
    {
        var values = new Dictionary<string, string?>
        {
            ["AdminBootstrap:Email"] = " admin@example.com ",
            ["AdminBootstrap:Password"] = "configured-password",
            ["AdminBootstrap:FullName"] = "Portfolio Admin"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var created = AdminBootstrapCredentials.TryCreate(configuration, out var credentials);

        Assert.True(created);
        Assert.Equal("admin@example.com", credentials.Email);
        Assert.Equal("configured-password", credentials.Password);
        Assert.Equal("Portfolio Admin", credentials.FullName);
    }

    [Fact]
    public void Admin_bootstrap_duplicate_check_is_case_insensitive()
    {
        Assert.True(AdminBootstrapCredentials.IsSameEmail("Admin@Example.com", "admin@example.com"));
        Assert.False(AdminBootstrapCredentials.IsSameEmail("other@example.com", "admin@example.com"));
    }
}
