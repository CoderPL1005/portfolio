using Npgsql;
using Portfolio.Api.Startup;
using Portfolio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector.EntityFrameworkCore;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Portfolio.IntegrationTests.PortfolioContent;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class DiscoveryMigrationStartupTests
{
    [PostgresFact]
    public async Task Concurrent_startups_hold_transaction_lock_through_all_migration_commands()
    {
        var configured=Environment.GetEnvironmentVariable("CHAT_QUOTA_TEST_CONNECTION") ?? throw new InvalidOperationException("Dedicated test connection required.");
        var cs=new NpgsqlConnectionStringBuilder(configured);
        if(cs.Host is not ("localhost" or "127.0.0.1") || cs.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true)
            throw new InvalidOperationException("Only local *_tests database allowed.");
        var schema="discovery_startup_"+Guid.NewGuid().ToString("N");cs.SearchPath=schema;
        await using var admin=new NpgsqlConnection(cs.ConnectionString);await admin.OpenAsync();
        try
        {
            await using(var create=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"; CREATE TABLE admin_users(id uuid PRIMARY KEY);",admin))await create.ExecuteNonQueryAsync();
            await using(var seed=Context(cs.ConnectionString))
            {
                await using(var history=new NpgsqlCommand(seed.GetService<IHistoryRepository>().GetCreateScript(),admin))await history.ExecuteNonQueryAsync();
                foreach(var migration in seed.Database.GetMigrations().Where(x=>x!=DiscoveryReleaseMigration.Candidates && x!=DiscoveryReleaseMigration.Reviews))
                {
                    await using var insert=new NpgsqlCommand("INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\",\"ProductVersion\") VALUES (@id,'8.0.11')",admin);
                    insert.Parameters.AddWithValue("id",migration);await insert.ExecuteNonQueryAsync();
                }
                // SearchPath alone does not set NpgsqlHistoryRepository.TableSchema.
                // Prove EF sees the seeded schema before exercising the concurrency path.
                Assert.Contains(DiscoveryReleaseMigration.Predecessor,await seed.Database.GetAppliedMigrationsAsync());
                Assert.Equal(new[]{DiscoveryReleaseMigration.Candidates,DiscoveryReleaseMigration.Reviews},
                    await seed.Database.GetPendingMigrationsAsync());
            }
            var probe=new MigrationLockProbe(cs.ConnectionString);
            await using var first=Context(cs.ConnectionString,probe);
            await using var second=Context(cs.ConnectionString,probe);
            var one=DiscoveryReleaseMigration.ApplyAsync(first,CancellationToken.None);
            await probe.FirstLocked.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var two=DiscoveryReleaseMigration.ApplyAsync(second,CancellationToken.None);
            await probe.SecondAttempting.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(two.IsCompleted); // Second connection is waiting on the database lock.
            probe.ReleaseFirst.TrySetResult();
            await Task.WhenAll(one,two).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(probe.CheckedCommands>=4);
            await using var verify=Context(cs.ConnectionString);
            Assert.Empty(await verify.Database.GetPendingMigrationsAsync());
        }
        finally {await using var drop=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",admin);await drop.ExecuteNonQueryAsync();}
    }
    private static ApplicationDbContext Context(string cs,IInterceptor? interceptor=null)
    {
        var schema=new NpgsqlConnectionStringBuilder(cs).SearchPath;
        if(string.IsNullOrWhiteSpace(schema))throw new InvalidOperationException("An isolated test schema is required.");
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs,x=>
        {
            x.UseVector();
            x.MigrationsHistoryTable("__EFMigrationsHistory",schema);
        });
        if(interceptor is not null)options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    [Fact]
    public void Isolated_fixture_qualifies_history_for_both_creation_and_existence_checks()
    {
        const string schema="discovery_startup_offline";
        using var db=Context($"Host=127.0.0.1;Port=1;Database=offline_design_time;Search Path={schema}");
        var history=db.GetService<IHistoryRepository>();
        Assert.Contains($"{schema}.\"__EFMigrationsHistory\"",history.GetCreateScript());
        var existsSql=Assert.IsType<string>(history.GetType().GetProperty("ExistsSql",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(history));
        Assert.Contains("n.nspname='"+schema+"'",existsSql);
        Assert.DoesNotContain("n.nspname='public'",existsSql);
    }
    private sealed class MigrationLockProbe(string connectionString):DbCommandInterceptor
    {
        private int locks;
        public int CheckedCommands;
        public TaskCompletionSource FirstLocked {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondAttempting {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(command.CommandText==DiscoveryReleaseMigration.LockSql && Interlocked.Increment(ref locks)==2)SecondAttempting.TrySetResult();
            if(command.CommandText.Contains("CREATE TABLE discovery_",StringComparison.Ordinal) ||
                (command.CommandText.StartsWith("INSERT INTO ",StringComparison.Ordinal) && command.CommandText.Contains("\"__EFMigrationsHistory\"",StringComparison.Ordinal)))
            {
                Assert.NotNull(command.Transaction);
                await using var other=new NpgsqlConnection(connectionString);await other.OpenAsync(ct);
                await using var test=new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(741204,445)",other);
                Assert.False((bool)(await test.ExecuteScalarAsync(ct))!);
                Interlocked.Increment(ref CheckedCommands);
            }
            return result;
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command,CommandExecutedEventData data,int result,CancellationToken ct=default)
        {
            if(command.CommandText==DiscoveryReleaseMigration.LockSql && !FirstLocked.Task.IsCompleted)
            {FirstLocked.TrySetResult();await ReleaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(20),ct);}
            return result;
        }
    }
    [Fact] public void Release_migrations_support_the_outer_transaction_required_by_the_database_lock()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=offline_design_time", x=>x.UseVector()).Options);
        var assembly=db.GetService<IMigrationsAssembly>();
        foreach(var id in new[]{DiscoveryReleaseMigration.Candidates,DiscoveryReleaseMigration.Reviews})
        {
            var migration=assembly.CreateMigration(assembly.Migrations[id],db.Database.ProviderName!);
            var commands=db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
            Assert.NotEmpty(commands);Assert.All(commands,c=>Assert.False(c.TransactionSuppressed));
            Assert.DoesNotContain(commands,c=>c.CommandText.Contains("DROP TABLE",StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(commands,c=>c.CommandText.Contains("ALTER TABLE",StringComparison.OrdinalIgnoreCase));
        }
    }
    [Theory]
    [InlineData(true,"true",false,true)]
    [InlineData(false,"true",false,false)]
    [InlineData(true,null,false,false)]
    [InlineData(true,"false",false,false)]
    [InlineData(true,"true",true,false)]
    public void Only_Render_production_startup_runs_release_migrations(bool production,string? render,bool seed,bool expected) =>
        Assert.Equal(expected,DiscoveryMigrationStartup.ShouldRun(production,render,seed));

    [Fact] public void Only_reviewed_pending_migrations_are_allowed_on_existing_baseline()
    {
        DiscoveryReleaseMigration.ValidateHistory([DiscoveryReleaseMigration.Predecessor],[DiscoveryReleaseMigration.Candidates,DiscoveryReleaseMigration.Reviews]);
        DiscoveryReleaseMigration.ValidateHistory([DiscoveryReleaseMigration.Predecessor,DiscoveryReleaseMigration.Candidates],[DiscoveryReleaseMigration.Reviews]);
        DiscoveryReleaseMigration.ValidateHistory([DiscoveryReleaseMigration.Predecessor,DiscoveryReleaseMigration.Candidates,DiscoveryReleaseMigration.Reviews],[]);
        Assert.Throws<InvalidOperationException>(()=>DiscoveryReleaseMigration.ValidateHistory([], [DiscoveryReleaseMigration.Candidates]));
        Assert.Throws<InvalidOperationException>(()=>DiscoveryReleaseMigration.ValidateHistory([DiscoveryReleaseMigration.Predecessor],["unreviewed_migration"]));
        Assert.Contains("pg_advisory_xact_lock",DiscoveryReleaseMigration.LockSql);
        Assert.DoesNotContain("pg_advisory_lock(",DiscoveryReleaseMigration.LockSql);
    }
    [Fact] public void Failure_codes_do_not_expose_provider_details_or_credentials()
    {
        var secret="private-provider-details";
        Assert.Equal("POSTGRES_42501",DiscoveryReleaseMigration.SafeFailureCode(new PostgresException(secret,"ERROR","ERROR","42501")));
        Assert.Equal("DATABASE_CONNECTION_FAILED",DiscoveryReleaseMigration.SafeFailureCode(new NpgsqlException(secret)));
        Assert.Equal("MIGRATION_FAILED",DiscoveryReleaseMigration.SafeFailureCode(new Exception(secret)));
    }
}
