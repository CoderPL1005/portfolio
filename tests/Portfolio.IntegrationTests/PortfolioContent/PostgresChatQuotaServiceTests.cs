using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Infrastructure.ChatProtection;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.IntegrationTests.PortfolioContent;

public sealed class PostgresChatQuotaServiceTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";
    private static readonly DateTimeOffset InitialTime =
        new(2026, 9, 3, 23, 59, 59, TimeSpan.Zero);

    [PostgresFact]
    public async Task Daily_quota_is_per_visitor_and_resets_at_utc_midnight()
    {
        var sessions = await ResetSchemaAsync(2);
        var clock = new MutableTimeProvider(InitialTime);
        var settings = Settings(daily: 2);

        await ReserveAsync(sessions[0], "ip:visitor-a", settings, clock);
        await ReserveAsync(sessions[0], "ip:visitor-a", settings, clock);
        var rejected = await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            ReserveAsync(sessions[0], "ip:visitor-a", settings, clock));
        Assert.Equal("CHAT_DAILY_LIMIT_REACHED", rejected.Code);

        await ReserveAsync(sessions[1], "ip:visitor-b", settings, clock);
        clock.UtcNow = new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
        await ReserveAsync(sessions[0], "ip:visitor-a", settings, clock);

        Assert.Equal(2, await CountAsync(new DateOnly(2026, 9, 3), "ip:visitor-a"));
        Assert.Equal(1, await CountAsync(new DateOnly(2026, 9, 4), "ip:visitor-a"));
    }

    [PostgresFact]
    public async Task Session_quota_counts_user_reservations_and_rejects_the_next_request()
    {
        var session = (await ResetSchemaAsync(1))[0];
        var settings = Settings(session: 2);
        var clock = new MutableTimeProvider(InitialTime);

        await ReserveAsync(session, "ip:visitor", settings, clock);
        await ReserveAsync(session, "ip:visitor", settings, clock);
        var rejected = await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            ReserveAsync(session, "ip:visitor", settings, clock));

        Assert.Equal("CHAT_SESSION_LIMIT_REACHED", rejected.Code);
        Assert.Equal(2, await SessionCountAsync(session));
    }

    [PostgresFact]
    public async Task Global_quota_aggregates_visitors_without_partially_consuming_rejected_request()
    {
        var sessions = await ResetSchemaAsync(3);
        var settings = Settings(global: 2);
        var clock = new MutableTimeProvider(InitialTime);

        await ReserveAsync(sessions[0], "ip:a", settings, clock);
        await ReserveAsync(sessions[1], "ip:b", settings, clock);
        var rejected = await Assert.ThrowsAsync<TooManyRequestsException>(() =>
            ReserveAsync(sessions[2], "ip:c", settings, clock));

        Assert.Equal("CHAT_GLOBAL_LIMIT_REACHED", rejected.Code);
        Assert.Equal(2, await CountAsync(new DateOnly(2026, 9, 3), PostgresChatQuotaService.GlobalVisitorKey));
        Assert.Equal(0, await CountAsync(new DateOnly(2026, 9, 3), "ip:c"));
        Assert.Equal(0, await SessionCountAsync(sessions[2]));
    }

    [PostgresFact]
    public async Task Concurrent_per_ip_reservations_do_not_overshoot()
    {
        var sessions = await ResetSchemaAsync(2);
        await SetUsageAsync("ip:shared", 19, 19);

        var results = await ReserveConcurrentlyAsync(
            sessions, ["ip:shared", "ip:shared"], Settings(daily: 20), new MutableTimeProvider(InitialTime));

        Assert.Single(results.Where(result => result is null));
        Assert.Single(results.Where(result => result == "CHAT_DAILY_LIMIT_REACHED"));
        Assert.Equal(20, await CountAsync(new DateOnly(2026, 9, 3), "ip:shared"));
        Assert.Equal(20, await CountAsync(new DateOnly(2026, 9, 3), PostgresChatQuotaService.GlobalVisitorKey));
    }

    [PostgresFact]
    public async Task Concurrent_global_reservations_do_not_overshoot_and_keep_counters_consistent()
    {
        var sessions = await ResetSchemaAsync(2);
        await SetUsageAsync("ip:existing", 149, 149);

        var results = await ReserveConcurrentlyAsync(
            sessions, ["ip:a", "ip:b"], Settings(global: 150), new MutableTimeProvider(InitialTime));

        Assert.Single(results.Where(result => result is null));
        Assert.Single(results.Where(result => result == "CHAT_GLOBAL_LIMIT_REACHED"));
        Assert.Equal(150, await CountAsync(new DateOnly(2026, 9, 3), PostgresChatQuotaService.GlobalVisitorKey));
        Assert.Equal(1, await SumVisitorCountsAsync(new DateOnly(2026, 9, 3), "ip:a", "ip:b"));
        Assert.Equal(1, await SumSessionCountsAsync(sessions));
    }

    [PostgresFact]
    public async Task Concurrent_session_reservations_do_not_overshoot()
    {
        var session = (await ResetSchemaAsync(1))[0];
        await SetSessionCountAsync(session, 19);

        var results = await ReserveConcurrentlyAsync(
            [session, session], ["ip:a", "ip:b"], Settings(session: 20), new MutableTimeProvider(InitialTime));

        Assert.Single(results.Where(result => result is null));
        Assert.Single(results.Where(result => result == "CHAT_SESSION_LIMIT_REACHED"));
        Assert.Equal(20, await SessionCountAsync(session));
        Assert.Equal(1, await CountAsync(new DateOnly(2026, 9, 3), PostgresChatQuotaService.GlobalVisitorKey));
    }

    private static async Task<string?[]> ReserveConcurrentlyAsync(
        Guid[] sessions,
        string[] visitors,
        ChatProtectionSettings settings,
        TimeProvider clock)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        async Task<string?> Attempt(int index)
        {
            if (Interlocked.Increment(ref started) == 2) ready.SetResult();
            await ready.Task;
            try
            {
                await ReserveAsync(sessions[index], visitors[index], settings, clock);
                return null;
            }
            catch (TooManyRequestsException error)
            {
                return error.Code;
            }
        }

        return await Task.WhenAll(Attempt(0), Attempt(1));
    }

    private static async Task ReserveAsync(
        Guid session,
        string visitor,
        ChatProtectionSettings settings,
        TimeProvider clock)
    {
        await using var context = CreateContext();
        var service = new PostgresChatQuotaService(context, Options.Create(settings), clock);
        await service.ReserveAsync(session, visitor);
    }

    private static ChatProtectionSettings Settings(int daily = 20, int session = 20, int global = 150) =>
        new()
        {
            DailyPerVisitorLimit = daily,
            SessionUserMessageLimit = session,
            GlobalDailyLimit = global,
            IpHashSecret = "postgres-test-hmac-secret-with-32-characters"
        };

    private static async Task<Guid[]> ResetSchemaAsync(int sessionCount)
    {
        await using var connection = new NpgsqlConnection(TestConnection());
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("""
            DROP TABLE IF EXISTS chat_usage_daily;
            DROP TABLE IF EXISTS chat_sessions;
            CREATE TABLE chat_sessions (
                id uuid PRIMARY KEY,
                public_session_id uuid NOT NULL UNIQUE,
                status character varying(20) NOT NULL,
                user_message_count integer NOT NULL DEFAULT 0 CHECK (user_message_count >= 0)
            );
            CREATE TABLE chat_usage_daily (
                usage_date date NOT NULL,
                visitor_key character varying(80) NOT NULL,
                accepted_message_count integer NOT NULL DEFAULT 0 CHECK (accepted_message_count >= 0),
                created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                updated_at timestamp with time zone NOT NULL DEFAULT NOW(),
                PRIMARY KEY (usage_date, visitor_key)
            );
            """, connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        var sessions = Enumerable.Range(0, sessionCount).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var session in sessions)
        {
            await using var insert = new NpgsqlCommand(
                "INSERT INTO chat_sessions (id, public_session_id, status) VALUES (@id, @id, 'ACTIVE');",
                connection);
            insert.Parameters.AddWithValue("id", session);
            await insert.ExecuteNonQueryAsync();
        }

        return sessions;
    }

    private static async Task SetUsageAsync(string visitor, int visitorCount, int globalCount)
    {
        await using var connection = new NpgsqlConnection(TestConnection());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO chat_usage_daily (usage_date, visitor_key, accepted_message_count)
            VALUES ('2026-09-03', @visitor, @visitor_count),
                   ('2026-09-03', 'global', @global_count);
            """, connection);
        command.Parameters.AddWithValue("visitor", visitor);
        command.Parameters.AddWithValue("visitor_count", visitorCount);
        command.Parameters.AddWithValue("global_count", globalCount);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetSessionCountAsync(Guid session, int count)
    {
        await using var connection = new NpgsqlConnection(TestConnection());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE chat_sessions SET user_message_count = @count WHERE public_session_id = @session;", connection);
        command.Parameters.AddWithValue("count", count);
        command.Parameters.AddWithValue("session", session);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(DateOnly date, string key)
    {
        await using var connection = new NpgsqlConnection(TestConnection());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT accepted_message_count FROM chat_usage_daily WHERE usage_date = @date AND visitor_key = @key;",
            connection);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("key", key);
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<int> SessionCountAsync(Guid session)
    {
        await using var connection = new NpgsqlConnection(TestConnection());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT user_message_count FROM chat_sessions WHERE public_session_id = @session;", connection);
        command.Parameters.AddWithValue("session", session);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> SumVisitorCountsAsync(DateOnly date, params string[] keys)
    {
        var total = 0;
        foreach (var key in keys) total += await CountAsync(date, key);
        return total;
    }

    private static async Task<int> SumSessionCountsAsync(IEnumerable<Guid> sessions)
    {
        var total = 0;
        foreach (var session in sessions) total += await SessionCountAsync(session);
        return total;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(TestConnection(), npgsql => npgsql.UseVector())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static string TestConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} was not configured.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Database?.EndsWith("_tests", StringComparison.OrdinalIgnoreCase) != true
            || builder.Host is not ("localhost" or "127.0.0.1"))
        {
            throw new InvalidOperationException("Chat quota tests require a local database whose name ends in _tests.");
        }

        return builder.ConnectionString;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHAT_QUOTA_TEST_CONNECTION")))
        {
            Skip = "Set CHAT_QUOTA_TEST_CONNECTION to a local *_tests PostgreSQL database.";
        }
    }
}
