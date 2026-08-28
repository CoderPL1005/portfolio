using Portfolio.Api.Startup;

namespace Portfolio.IntegrationTests.Api;

public sealed class DatabaseSeedModeTests
{
    [Theory]
    [InlineData(new[] { "--seed" }, true)]
    [InlineData(new[] { "--environment", "Development", "--seed" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--seed=false" }, false)]
    [InlineData(new[] { "--Seed" }, false)]
    public void Detects_only_the_explicit_seed_argument(string[] args, bool expected)
    {
        Assert.Equal(expected, DatabaseSeedMode.IsRequested(args));
    }

    [Fact]
    public void Removes_only_seed_mode_from_normal_host_configuration_arguments()
    {
        Assert.Equal(
            ["--environment", "Development"],
            DatabaseSeedMode.WithoutSeedArgument(["--environment", "Development", "--seed"]));
    }

    [Fact]
    public void Resolves_the_canonical_seed_from_a_nested_start_directory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Portfolio.sln"), string.Empty);
            var seedDirectory = Directory.CreateDirectory(Path.Combine(root, "seed"));
            var seedPath = Path.Combine(seedDirectory.FullName, "portfolio.seed.json");
            File.WriteAllText(seedPath, "{}");
            var nested = Directory.CreateDirectory(Path.Combine(root, "src", "Portfolio.Api", "bin", "Debug"));

            Assert.Equal(seedPath, DatabaseSeedMode.ResolveSeedPath(nested.FullName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Missing_seed_file_fails_clearly_before_any_seeding_can_run()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "Portfolio.sln"), string.Empty);

            var error = Assert.Throws<FileNotFoundException>(() => DatabaseSeedMode.ResolveSeedPath(root));

            Assert.Contains(Path.Combine("seed", "portfolio.seed.json"), error.Message);
            Assert.Contains("Portfolio.sln", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Seed_mode_startup_code_does_not_expose_a_migration_operation()
    {
        var publicMethods = typeof(DatabaseSeedMode).GetMethods()
            .Where(method => method.DeclaringType == typeof(DatabaseSeedMode))
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(nameof(DatabaseSeedMode.IsRequested), publicMethods);
        Assert.Contains(nameof(DatabaseSeedMode.ResolveSeedPath), publicMethods);
        Assert.DoesNotContain(publicMethods, method => method.Contains("Migrat", StringComparison.OrdinalIgnoreCase));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"portfolio-seed-mode-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
