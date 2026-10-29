using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;
using Portfolio.IntegrationTests.Authentication;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Opt-in only: existing localhost *_tests database, preinstalled vector/pgcrypto.
// No containers, image pulls, installations, production connections or migrations.
// Unique schema per case; future approved recovery model is created from actual EF
// definitions, not a synthetic substitute for production FK/unique constraints.
public sealed class DiscoveryExecutionRecoveryPostgresRedTests(AuthApiFactory root) : IClassFixture<AuthApiFactory>
{
    [RecoveryPostgresTheory]
    [InlineData("candidate")]
    [InlineData("audit")]
    [InlineData("receipt")]
    [InlineData("execution")]
    public async Task T12_PostgreSQL_failure_after_each_write_boundary_rolls_back_the_whole_recovery(string boundary)
    {
        await using var pg = await LocalRecoveryDatabase.Create(); using var s = await pg.Scenario(root);
        var preview = await s.Preview(); var before = await pg.Counts(); var state = await s.Snapshot();
        await pg.FailAfterWrite(boundary);
        using var response = await s.Client.PostAsJsonAsync(s.FinalizePath(), s.Request(preview));
        Assert.False(response.IsSuccessStatusCode); Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        // These inspect fresh database contexts/connections AFTER request disposal.
        Assert.Equal(before, await pg.Counts()); Assert.Equal(state, await s.Snapshot()); s.AssertOffline();
    }

    [RecoveryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task T12_PostgreSQL_cross_context_finalizers_commit_one_durable_recovery(bool sameOperation)
    {
        await using var pg = await LocalRecoveryDatabase.Create(); using var s = await pg.Scenario(root);
        var p = await s.Preview(); var op = Guid.NewGuid();
        // TestServer creates independent service scopes/DbContexts for HTTP requests.
        var replies = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => s.Client.PostAsJsonAsync(s.FinalizePath(), s.Request(p, sameOperation || i == 0 ? op : Guid.NewGuid()))));
        Assert.Contains(replies, r => r.IsSuccessStatusCode);
        Assert.All(replies, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict));
        if (!sameOperation) Assert.Single(replies.Where(r => r.IsSuccessStatusCode));
        await using var verify = pg.Context();
        Assert.Equal(7, await verify.DiscoveryCandidates.CountAsync()); Assert.Single(await verify.DiscoveryExecutionAttempts.ToListAsync());
        var e = await verify.DiscoveryExecutions.SingleAsync(); Assert.Equal("PARTIAL", e.State); Assert.Null(e.ResultHash); Assert.Null(e.ActiveKey);
        Assert.Equal(s.Version + 1, e.Version);
        var successful = replies.Where(r => r.IsSuccessStatusCode).ToArray();
        var receipt = await RecoveryScenario.Data(successful[0]);
        foreach (var other in successful.Skip(1)) Assert.Equal(receipt.GetRawText(), (await RecoveryScenario.Data(other)).GetRawText());
        var before = await pg.Counts(); var replay = await s.Finalize(p, receipt.GetProperty("operationId").GetGuid());
        Assert.Equal(receipt.GetRawText(), replay.GetRawText()); Assert.Equal(before, await pg.Counts()); s.AssertOffline();
    }

    [RecoveryPostgresFact]
    public async Task T12_PostgreSQL_enforces_observation_unique_and_raw_FK_constraints()
    {
        await using var pg = await LocalRecoveryDatabase.Create(); using var s = await pg.Scenario(root);
        // Require capability before using relational controls as recovery evidence.
        await s.Preview();
        await using (var db = pg.Context()) {
            var o = await db.DiscoveryExecutionObservations.AsNoTracking().FirstAsync();
            db.DiscoveryExecutionObservations.Add(new() { Id = Guid.NewGuid(), ExecutionId = o.ExecutionId, ItemIndex = o.ItemIndex,
                PayloadJson = o.PayloadJson, PayloadHash = o.PayloadHash, ContentHash = o.ContentHash });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = pg.Context()) {
            var o = await db.DiscoveryExecutionObservations.FirstAsync(); o.RawJobPostingId = Guid.NewGuid();
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using var verify = pg.Context(); Assert.Equal(7, await verify.DiscoveryExecutionObservations.CountAsync());
    }

    [RecoveryPostgresTheory]
    [InlineData("raw")]
    [InlineData("human-review")]
    public async Task T12_PostgreSQL_dependency_change_between_preview_and_finalize_cannot_be_lost(string dependency)
    {
        await using var pg = await LocalRecoveryDatabase.Create(); using var s = await pg.Scenario(root);
        var p = await s.Preview();
        if (dependency == "raw") await s.MutateSource("raw");
        else await s.AddNormalCandidate(reviewed: true, sameRun: true);
        var before = await s.Snapshot();
        using var response = await s.Client.PostAsJsonAsync(s.FinalizePath(), s.Request(p));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal(before, await s.Snapshot()); s.AssertOffline();
    }
}

internal sealed class RecoveryPostgresFactAttribute : FactAttribute
{
    public RecoveryPostgresFactAttribute() { if (!LocalRecoveryDatabase.Enabled) Skip = LocalRecoveryDatabase.Prerequisite; }
}
internal sealed class RecoveryPostgresTheoryAttribute : TheoryAttribute
{
    public RecoveryPostgresTheoryAttribute() { if (!LocalRecoveryDatabase.Enabled) Skip = LocalRecoveryDatabase.Prerequisite; }
}
internal sealed class LocalRecoveryDatabase : IAsyncDisposable
{
    internal const string Prerequisite = "NOT_VERIFIED: opt-in existing local *_tests PostgreSQL with vector/pgcrypto; production finalize BLOCKED.";
    internal static bool Enabled => Environment.GetEnvironmentVariable("DISCOVERY_RECOVERY_POSTGRES_OPT_IN") == "1" &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISCOVERY_RECOVERY_TEST_CONNECTION"));
    private readonly string connection;
    private readonly string schema;
    private LocalRecoveryDatabase(string connection, string schema) { this.connection = connection; this.schema = schema; }
    internal ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection, o => o.UseVector()).Options);
    internal static async Task<LocalRecoveryDatabase> Create()
    {
        if (!Enabled) throw new InvalidOperationException("Local PostgreSQL opt-in required.");
        var b = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("DISCOVERY_RECOVERY_TEST_CONNECTION"));
        if (b.Host is not ("127.0.0.1" or "localhost") || b.Database?.EndsWith("_tests", StringComparison.OrdinalIgnoreCase) != true || !string.IsNullOrEmpty(b.SearchPath))
            throw new InvalidOperationException("Only explicit localhost *_tests connection with no preset schema is allowed.");
        b.Pooling = false; var schema = "recovery_red_" + Guid.NewGuid().ToString("N"); b.SearchPath = schema + ",public";
        var pg = new LocalRecoveryDatabase(b.ConnectionString, schema);
        await using var sql = new NpgsqlConnection(pg.connection); await sql.OpenAsync();
        await using (var check = new NpgsqlCommand("SELECT count(*) FROM pg_extension WHERE extname IN ('vector','pgcrypto')", sql))
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) != 2) throw new InvalidOperationException("Preinstalled vector/pgcrypto required; tests never install extensions.");
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", sql)) await create.ExecuteNonQueryAsync();
        try {
            await using var db = pg.Context(); var script = db.Database.GenerateCreateScript();
            // Extension statements are removed only after verifying BOTH already
            // exist. No extension/package installation and no migration apply.
            script = string.Join('\n', script.Split('\n').Where(line => !line.TrimStart().StartsWith("CREATE EXTENSION", StringComparison.OrdinalIgnoreCase)));
            await using (var tables = new NpgsqlCommand(script, sql)) await tables.ExecuteNonQueryAsync();
            db.AdminUsers.Add(new() { Id = AuthApiFactory.AdminId, Email = "synthetic@example.invalid", PasswordHash = "SYNTHETIC_NOT_A_PASSWORD", IsActive = true });
            await db.SaveChangesAsync(); return pg;
        }
        catch { await pg.DisposeAsync(); throw; }
    }
    internal Task<RecoveryScenario> Scenario(AuthApiFactory root) => RecoveryScenario.Create(root, database: (services, fault) => {
        services.RemoveAll<ApplicationDbContext>();
        services.AddScoped(_ => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection, o => o.UseVector()).AddInterceptors(fault).Options));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
    });
    internal async Task<string> Counts() {
        await using var db = Context(); var values = new List<string>();
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        foreach (var table in db.Model.GetEntityTypes().Select(x => x.GetTableName()).Where(x => x is not null).Distinct().OrderBy(x => x, StringComparer.Ordinal)) {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{schema}\".{Quote(table!)}", sql);
            values.Add(table + ":" + await command.ExecuteScalarAsync());
        }
        return string.Join(";", values);
    }
    internal async Task FailAfterWrite(string boundary) {
        await using var db = Context();
        var table = boundary switch {
            "candidate" => "discovery_candidates",
            "execution" => "discovery_executions",
            "audit" => db.Model.GetEntityTypes().FirstOrDefault(x => x.ClrType.Name.Contains("RecoveryAudit", StringComparison.Ordinal))?.GetTableName() ?? "discovery_execution_events",
            _ => db.Model.GetEntityTypes().FirstOrDefault(x => x.ClrType.Name.Contains("RecoveryReceipt", StringComparison.Ordinal))?.GetTableName() ?? "discovery_execution_events"
        };
        // Receipt may be a separate entity or a durable record in the existing
        // event abstraction. Do not force a new production table merely for tests.
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        var statement = $"""
            CREATE FUNCTION "{schema}".fail_recovery_write() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'SYNTHETIC_RECOVERY_WRITE_FAILURE'; END $$;
            CREATE TRIGGER fail_recovery_write AFTER {(boundary == "execution" ? "UPDATE" : "INSERT")} ON "{schema}".{Quote(table!)}
            FOR EACH ROW EXECUTE FUNCTION "{schema}".fail_recovery_write();
            """;
        await using var command = new NpgsqlCommand(statement, sql); await command.ExecuteNonQueryAsync();
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    public async ValueTask DisposeAsync() {
        // Exact generated schema only; never an existing/shared schema/database.
        if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^recovery_red_[a-f0-9]{32}$")) throw new InvalidOperationException("Invalid disposable schema.");
        await using var sql = new NpgsqlConnection(connection); await sql.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", sql); await command.ExecuteNonQueryAsync();
    }
}
