using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Abstractions.Persistence;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Opt-in LOCAL disposable *_tests database only. Never application configuration.
public sealed class DiscoveryCleanupPostgresTests
{
    private const string FormerRun="fresh-demo-20261027-a2";
    [PostgresFact] public Task Concurrent_scoped_cleanup_commits_exactly_one_and_preserves_unselected_run()=>WithDatabase(async cs=>
    {
        await using var seed=Context(cs);var backup=await Export(seed);
        await using var one=Context(cs);await using var two=Context(cs);
        var results=await Task.WhenAll(Attempt(one),Attempt(two));Assert.Single(results.Where(x=>x));Assert.Single(results.Where(x=>!x));
        await using var verify=Context(cs);Assert.Equal(FormerRun,(await verify.DiscoveryCandidates.SingleAsync()).RunId);
        Assert.Empty(await verify.DiscoveryCandidateReviews.ToListAsync());Assert.Empty(await verify.DiscoveryCandidateReviewEvents.ToListAsync());
        async Task<bool> Attempt(ApplicationDbContext db){try{await new DeleteDiscoveryRunsHandler(db,new Actor(),new NpgsqlDiscoveryCleanupTransactionFactory(db)).HandleAsync(Command(backup));return true;}
            catch(ConflictException e){Assert.Equal("DISCOVERY_CLEANUP_STALE",e.Code);return false;}}
    });
    [PostgresFact] public Task Commit_failure_rolls_back_all_three_tables_after_real_deletes()=>WithDatabase(async cs=>
    {
        await using var db=Context(cs);var backup=await Export(db);var factory=new FailCommitFactory(new NpgsqlDiscoveryCleanupTransactionFactory(db));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new DeleteDiscoveryRunsHandler(db,new Actor(),factory).HandleAsync(Command(backup)));
        Assert.True(factory.ReachedCommit);await using var verify=Context(cs);
        Assert.Equal(backup.Fingerprint,DiscoveryCleanup.Hash(await DiscoveryCleanup.ReadAsync(verify,["legacy-pg"],default)));
        Assert.Equal(2,await verify.DiscoveryCandidates.CountAsync());
    });
    private static DeleteDiscoveryRunsCommand Command(DiscoveryCleanupBackup b)=>new(["legacy-pg"],b.Fingerprint,b,true,DiscoveryCleanup.Confirmation);
    private static Task<DiscoveryCleanupBackup> Export(ApplicationDbContext db)=>new ExportDiscoveryCleanupBackupHandler(db,new Actor(),new NpgsqlDiscoveryCleanupTransactionFactory(db)).HandleAsync(new(["legacy-pg"]));
    private static ApplicationDbContext Context(string cs)=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs,o=>o.UseVector()).Options);
    private static async Task WithDatabase(Func<string,Task> test)
    {
        var builder=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CHAT_QUOTA_TEST_CONNECTION")??throw new InvalidOperationException("Dedicated local test DB required."));
        if(builder.Host is not ("localhost" or "127.0.0.1")||builder.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true)
            throw new InvalidOperationException("Only local *_tests database allowed.");
        var schema="discovery_cleanup_"+Guid.NewGuid().ToString("N");builder.SearchPath=schema;var cs=builder.ConnectionString;
        await using var connection=new NpgsqlConnection(cs);await connection.OpenAsync();
        try
        {
            await using(var cmd=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"; CREATE TABLE admin_users(id uuid PRIMARY KEY)",connection))await cmd.ExecuteNonQueryAsync();
            await using var db=Context(cs);var assembly=db.GetService<IMigrationsAssembly>();
            foreach(var suffix in new[]{"_AddDiscoveryReviewCandidates","_AddDiscoveryHumanReviews"})
            {
                var key=assembly.Migrations.Keys.Single(k=>k.EndsWith(suffix,StringComparison.Ordinal));var migration=assembly.CreateMigration(assembly.Migrations[key],db.Database.ProviderName!);
                foreach(var sql in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations))
                {await using var command=new NpgsqlCommand(sql.CommandText,connection);await command.ExecuteNonQueryAsync();}
            }
            await using(var cmd=new NpgsqlCommand("INSERT INTO admin_users(id) VALUES (@id)",connection)){cmd.Parameters.AddWithValue("id",Actor.Id);await cmd.ExecuteNonQueryAsync();}
            foreach(var run in new[]{"legacy-pg",FormerRun})db.DiscoveryCandidates.Add(new(){Id=Guid.NewGuid(),RunId=run,Query="Backend",PageNumber=1,ItemIndex=0,
                ContentStatus="VALID_RECRUITMENT",TargetStatus="NOT_MATCHED",Payload="{}",PayloadHash=new string('a',64),IngestedAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync();var id=(await db.DiscoveryCandidates.SingleAsync(c=>c.RunId=="legacy-pg")).Id;
            await new SaveDiscoveryHumanReviewHandler(db,new Actor(),TimeProvider.System,new NpgsqlDiscoveryReviewConflictDetector()).HandleAsync(new(id,new(0,"UNCERTAIN",null,null,"Preserve full history")));
            await test(cs);
        }
        finally{await using var cleanup=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",connection);await cleanup.ExecuteNonQueryAsync();}
    }
    private sealed class Actor:ICurrentUser{public static readonly Guid Id=Guid.Parse("11111111-1111-1111-1111-111111111111");public bool IsAuthenticated=>true;public Guid? AdminUserId=>Id;public string? Email=>null;}
    private sealed class FailCommitFactory(IDiscoveryCleanupTransactionFactory inner):IDiscoveryCleanupTransactionFactory
    {
        public bool ReachedCommit;
        public async Task<IDiscoveryCleanupTransaction> BeginAsync(CancellationToken ct=default)=>new Fail(await inner.BeginAsync(ct),this);
        public Task<IJobDataResetTransaction> BeginResetAllAsync(CancellationToken ct=default)=>inner.BeginResetAllAsync(ct);
        private sealed class Fail(IDiscoveryCleanupTransaction inner,FailCommitFactory owner):IDiscoveryCleanupTransaction
        {public Task CommitAsync(CancellationToken ct=default){owner.ReachedCommit=true;throw new InvalidOperationException("Synthetic commit failure");}public ValueTask DisposeAsync()=>inner.DisposeAsync();}
    }
}
