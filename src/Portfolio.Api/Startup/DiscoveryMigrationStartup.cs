using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Api.Startup;

public static class DiscoveryMigrationStartup
{
    public static bool ShouldRun(bool production, string? render, bool seedRequested) =>
        production && string.Equals(render, "true", StringComparison.OrdinalIgnoreCase) && !seedRequested;

    public static async Task RunAsync(WebApplication app)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            await DiscoveryReleaseMigration.ApplyAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), timeout.Token);
            app.Logger.LogInformation("DISCOVERY_MIGRATIONS_READY: B44/B45 schema committed before API startup.");
        }
        catch (Exception e)
        {
            var code = DiscoveryReleaseMigration.SafeFailureCode(e);
            app.Logger.LogCritical("DISCOVERY_MIGRATIONS_FAILED: {SafeCode}. API will not start.", code);
            // Deliberately no inner exception: the unhandled startup exception must not expose secrets.
            throw new InvalidOperationException("DISCOVERY_MIGRATIONS_FAILED: " + code);
        }
    }
}
