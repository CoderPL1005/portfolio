using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Abstractions.Authentication;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Application.Features.JobHunting;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

// Environment guarded; never fallback to application/production connection.
public sealed class DiscoveryHumanReviewConcurrencyPostgresTests
{
    [PostgresFact] public Task Concurrent_first_reviews_commit_one_current_and_one_event() => Race(false);
    [PostgresFact] public Task Concurrent_edits_commit_one_next_version_and_no_orphan_event() => Race(true);
    private static async Task Race(bool edit)
    {
        var raw = Environment.GetEnvironmentVariable("CHAT_QUOTA_TEST_CONNECTION") ?? throw new InvalidOperationException("Dedicated test connection required.");
        var builder = new NpgsqlConnectionStringBuilder(raw);
        if(builder.Host is not ("localhost" or "127.0.0.1") || builder.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true)
            throw new InvalidOperationException("Only local *_tests database is allowed.");
        var schema="discovery_review_"+Guid.NewGuid().ToString("N");
        builder.SearchPath=schema;
        var connectionString=builder.ConnectionString;
        await using var connection=new NpgsqlConnection(connectionString);await connection.OpenAsync();
        try
        {
            await using(var cmd=new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"; CREATE TABLE admin_users(id uuid PRIMARY KEY);",connection)) await cmd.ExecuteNonQueryAsync();
            await using var seed=Context(connectionString);
            // Actual production mapping/migration operations, not a stale hand-built review schema.
            var assembly=seed.GetService<IMigrationsAssembly>();
            var key=assembly.Migrations.Keys.Single(x=>x.EndsWith("_AddDiscoveryReviewCandidates",StringComparison.Ordinal));
            var b44=assembly.CreateMigration(assembly.Migrations[key],seed.Database.ProviderName!);
            var next=assembly.Migrations.Keys.Single(x=>x.EndsWith("_AddDiscoveryHumanReviews",StringComparison.Ordinal));
            var b45=assembly.CreateMigration(assembly.Migrations[next],seed.Database.ProviderName!);
            foreach(var sql in seed.GetService<IMigrationsSqlGenerator>().Generate(b44.UpOperations.Concat(b45.UpOperations).ToArray()))
            {await using var cmd=new NpgsqlCommand(sql.CommandText,connection);await cmd.ExecuteNonQueryAsync();}
            await using (var origin = new NpgsqlCommand("ALTER TABLE discovery_candidates ADD COLUMN candidate_origin varchar(40) NOT NULL DEFAULT 'CLASSIFIER'", connection)) await origin.ExecuteNonQueryAsync();
            await using(var cmd=new NpgsqlCommand("INSERT INTO admin_users(id) VALUES (@id)",connection))
            {cmd.Parameters.AddWithValue("id",Actor.Id);await cmd.ExecuteNonQueryAsync();}
            var id=Guid.NewGuid();
            seed.DiscoveryCandidates.Add(new DiscoveryCandidate{Id=id,RunId="synthetic-pg",Query="Backend",PageNumber=1,ItemIndex=0,
                ContentStatus="VALID_RECRUITMENT",TargetStatus="NOT_MATCHED",Payload="{}",PayloadHash=new string('a',64),IngestedAt=DateTimeOffset.UtcNow});
            await seed.SaveChangesAsync();
            if(edit) await Handler(seed).HandleAsync(new(id,new(0,"CONFIRMED",null,null,"Initial")));
            var barrier=new ReadBarrier();
            await using var one=Context(connectionString,barrier);await using var two=Context(connectionString,barrier);
            var results=await Task.WhenAll(Attempt(one,"First"),Attempt(two,"Second"));
            Assert.Single(results.Where(x=>x));Assert.Single(results.Where(x=>!x));
            await using var verify=Context(connectionString);
            var current=await verify.DiscoveryCandidateReviews.SingleAsync();Assert.Equal(edit?2:1,current.Version);
            Assert.Equal(edit?2:1,await verify.DiscoveryCandidateReviewEvents.CountAsync());
            var latest=await verify.DiscoveryCandidateReviewEvents.SingleAsync(x=>x.Version==current.Version);
            Assert.Equal(current.Notes,latest.Notes);
            Assert.Equal(new string('a',64),(await verify.DiscoveryCandidates.SingleAsync()).PayloadHash);
            async Task<bool> Attempt(ApplicationDbContext db,string notes)
            {
                try {await Handler(db).HandleAsync(new(id,new(edit?1:0,"UNCERTAIN",null,null,notes)));return true;}
                catch(ConflictException e){Assert.Equal("DISCOVERY_REVIEW_VERSION_CONFLICT",e.Code);return false;}
            }
        }
        finally { await using var cleanup=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE",connection);await cleanup.ExecuteNonQueryAsync(); }
    }
    private static ApplicationDbContext Context(string cs, IInterceptor? interceptor=null)
    {
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs,x=>x.UseVector());
        if(interceptor is not null)options.AddInterceptors(interceptor);
        return new(options.Options);
    }
    private static SaveDiscoveryHumanReviewHandler Handler(ApplicationDbContext db)=>new(db,new Actor(),TimeProvider.System,new NpgsqlDiscoveryReviewConflictDetector());
    private sealed class Actor:ICurrentUser
    {
        internal static readonly Guid Id=Guid.Parse("11111111-1111-1111-1111-111111111111");
        public bool IsAuthenticated=>true;public Guid? AdminUserId=>Id;public string? Email=>null;
    }
    private sealed class ReadBarrier:DbCommandInterceptor
    {
        private int arrivals;private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken ct=default)
        {
            if(command.CommandText.Contains("FROM discovery_candidate_reviews",StringComparison.Ordinal))
            {
                if(Interlocked.Increment(ref arrivals)==2)ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15),ct);
            }
            return result;
        }
    }
}
