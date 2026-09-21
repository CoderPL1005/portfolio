using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Domain.Entities;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class CandidatePreferencesPostgresTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";

    [PostgresFact]
    public async Task Singleton_and_optimistic_concurrency_are_enforced_by_postgresql()
    {
        var schema = "candidate_preferences_" + Guid.NewGuid().ToString("N");
        var cs = Connection(schema); await CreateAsync(cs, schema);
        try
        {
            var id = Guid.NewGuid();
            await using (var seed = Context(cs)) { seed.CandidateJobPreferences.Add(Entity(id)); await seed.SaveChangesAsync(); }
            await using var first = Context(cs); await using var second = Context(cs);
            var a = await first.CandidateJobPreferences.SingleAsync(); var b = await second.CandidateJobPreferences.SingleAsync();
            a.Version++; a.UpdatedAt = DateTimeOffset.UtcNow; await first.SaveChangesAsync();
            b.Version++; b.UpdatedAt = DateTimeOffset.UtcNow;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
            await using var duplicate = Context(cs); duplicate.CandidateJobPreferences.Add(Entity(Guid.NewGuid()));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }
        finally { await DropAsync(Connection(), schema); }
    }

    private static CandidateJobPreferences Entity(Guid id) => new()
    {
        Id=id,SingletonKey="CURRENT",TargetRoles=JsonDocument.Parse("[]"),PreferredTechnologies=JsonDocument.Parse("[]"),
        AcceptableLocations=JsonDocument.Parse("[]"),WorkplaceTypes=JsonDocument.Parse("[]"),EmploymentTypes=JsonDocument.Parse("[]"),
        Version=1,CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow
    };
    private static ApplicationDbContext Context(string cs) => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs,o=>o.UseVector()).Options);
    private static async Task CreateAsync(string cs,string schema){await using var c=new NpgsqlConnection(cs);await c.OpenAsync();await using var cmd=new NpgsqlCommand($$"""
        CREATE SCHEMA "{{schema}}";
        CREATE TABLE candidate_job_preferences (
          id uuid PRIMARY KEY, singleton_key varchar(20) NOT NULL,
          target_roles jsonb NOT NULL, preferred_technologies jsonb NOT NULL, acceptable_locations jsonb NOT NULL,
          workplace_types jsonb NOT NULL, employment_types jsonb NOT NULL, minimum_salary numeric(18,2) NULL,
          salary_currency varchar(3) NULL, salary_period varchar(30) NULL, version integer NOT NULL,
          created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
          CONSTRAINT ck_candidate_job_preferences_singleton CHECK (singleton_key = 'CURRENT'));
        CREATE UNIQUE INDEX uq_candidate_job_preferences_singleton_key ON candidate_job_preferences(singleton_key);
        """,c);await cmd.ExecuteNonQueryAsync();}
    private static async Task DropAsync(string cs,string schema){await using var c=new NpgsqlConnection(cs);await c.OpenAsync();await using var cmd=new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;",c);await cmd.ExecuteNonQueryAsync();}
    private static string Connection(string? schema=null){var value=Environment.GetEnvironmentVariable(ConnectionVariable)??throw new InvalidOperationException($"{ConnectionVariable} was not configured.");var b=new NpgsqlConnectionStringBuilder(value);if(b.Database?.EndsWith("_tests",StringComparison.OrdinalIgnoreCase)!=true||b.Host is not("localhost" or "127.0.0.1"))throw new InvalidOperationException("Candidate preferences tests require a local *_tests PostgreSQL database.");if(schema is not null)b.SearchPath=schema;return b.ConnectionString;}
}
