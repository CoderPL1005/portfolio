namespace Portfolio.Api.Startup;

public static class DatabaseSeedMode
{
    public const string Argument = "--seed";
    private static readonly string SeedRelativePath = Path.Combine("seed", "portfolio.seed.json");

    public static bool IsRequested(IEnumerable<string> args) =>
        args.Any(argument => string.Equals(argument, Argument, StringComparison.Ordinal));

    public static string[] WithoutSeedArgument(IEnumerable<string> args) =>
        args.Where(argument => !string.Equals(argument, Argument, StringComparison.Ordinal)).ToArray();

    public static string ResolveSeedPath(params string?[] startPaths)
    {
        var searched = new List<string>();

        foreach (var startPath in startPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var directory = new DirectoryInfo(Path.GetFullPath(startPath!));
            if (!directory.Exists && directory.Parent is not null)
            {
                directory = directory.Parent;
            }

            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, SeedRelativePath);
                searched.Add(candidate);
                if (File.Exists(Path.Combine(directory.FullName, "Portfolio.sln")) && File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException(
            $"Seed mode could not find the canonical '{SeedRelativePath}' beneath a repository root containing Portfolio.sln. " +
            $"Searched: {string.Join(", ", searched.Distinct(StringComparer.OrdinalIgnoreCase))}");
    }
}
