using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Options;
using Portfolio.Application.Common.Abstractions.Chat;
using Portfolio.Application.Common.Exceptions;
using Portfolio.Infrastructure.Persistence;

namespace Portfolio.Infrastructure.ChatProtection;

public sealed class PostgresChatQuotaService(
    ApplicationDbContext db,
    IOptions<ChatProtectionSettings> options,
    TimeProvider clock) : IChatQuotaService
{
    public const string GlobalVisitorKey = "global";

    public async Task ReserveAsync(
        Guid publicSessionId,
        string visitorKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(visitorKey) || !visitorKey.StartsWith("ip:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Chat visitor identity is unavailable.");
        }

        var settings = options.Value;
        var now = clock.GetUtcNow();
        var usageDate = DateOnly.FromDateTime(now.UtcDateTime);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ReserveSessionAsync(
                connection,
                transaction,
                publicSessionId,
                settings.SessionUserMessageLimit,
                cancellationToken);
            await EnsureUsageRowsAsync(
                connection,
                transaction,
                usageDate,
                visitorKey,
                now,
                cancellationToken);
            var counts = await LockUsageRowsAsync(
                connection,
                transaction,
                usageDate,
                visitorKey,
                cancellationToken);

            if (counts[visitorKey] >= settings.DailyPerVisitorLimit)
            {
                throw new TooManyRequestsException(
                    "CHAT_DAILY_LIMIT_REACHED",
                    "Daily chat limit reached. Please try again tomorrow.");
            }

            if (counts[GlobalVisitorKey] >= settings.GlobalDailyLimit)
            {
                throw new TooManyRequestsException(
                    "CHAT_GLOBAL_LIMIT_REACHED",
                    "The portfolio assistant has reached today's usage limit. Please try again later.");
            }

            await IncrementUsageRowsAsync(
                connection,
                transaction,
                usageDate,
                visitorKey,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task ReserveSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid publicSessionId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE chat_sessions
            SET user_message_count = user_message_count + 1
            WHERE public_session_id = @session_id
              AND status = 'ACTIVE'
              AND user_message_count < @limit
            RETURNING id;
            """, connection, transaction);
        command.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, publicSessionId);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit);
        if (await command.ExecuteScalarAsync(cancellationToken) is not null)
        {
            return;
        }

        await using var statusCommand = new NpgsqlCommand("""
            SELECT status, user_message_count
            FROM chat_sessions
            WHERE public_session_id = @session_id
            FOR UPDATE;
            """, connection, transaction);
        statusCommand.Parameters.AddWithValue("session_id", NpgsqlDbType.Uuid, publicSessionId);
        await using var reader = await statusCommand.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new NotFoundException("CHAT_SESSION_NOT_FOUND", "Chat session was not found.");
        }

        if (reader.GetString(0) != "ACTIVE")
        {
            throw new ConflictException("CHAT_SESSION_CLOSED", "Chat session is closed.");
        }

        throw new TooManyRequestsException(
            "CHAT_SESSION_LIMIT_REACHED",
            "This chat session has reached its message limit. Please start a new session.");
    }

    private static async Task EnsureUsageRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateOnly usageDate,
        string visitorKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO chat_usage_daily
                (usage_date, visitor_key, accepted_message_count, created_at, updated_at)
            VALUES
                (@usage_date, @global_key, 0, @now, @now),
                (@usage_date, @visitor_key, 0, @now, @now)
            ON CONFLICT (usage_date, visitor_key) DO NOTHING;
            """, connection, transaction);
        AddUsageParameters(command, usageDate, visitorKey, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, int>> LockUsageRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateOnly usageDate,
        string visitorKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT visitor_key, accepted_message_count
            FROM chat_usage_daily
            WHERE usage_date = @usage_date
              AND visitor_key IN (@global_key, @visitor_key)
            ORDER BY visitor_key
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("usage_date", NpgsqlDbType.Date, usageDate);
        command.Parameters.AddWithValue("global_key", NpgsqlDbType.Varchar, GlobalVisitorKey);
        command.Parameters.AddWithValue("visitor_key", NpgsqlDbType.Varchar, visitorKey);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0), reader.GetInt32(1));
        }

        return result;
    }

    private static async Task IncrementUsageRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateOnly usageDate,
        string visitorKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE chat_usage_daily
            SET accepted_message_count = accepted_message_count + 1,
                updated_at = @now
            WHERE usage_date = @usage_date
              AND visitor_key IN (@global_key, @visitor_key);
            """, connection, transaction);
        AddUsageParameters(command, usageDate, visitorKey, now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 2)
        {
            throw new InvalidOperationException("Chat quota reservation failed.");
        }
    }

    private static void AddUsageParameters(
        NpgsqlCommand command,
        DateOnly usageDate,
        string visitorKey,
        DateTimeOffset now)
    {
        command.Parameters.AddWithValue("usage_date", NpgsqlDbType.Date, usageDate);
        command.Parameters.AddWithValue("global_key", NpgsqlDbType.Varchar, GlobalVisitorKey);
        command.Parameters.AddWithValue("visitor_key", NpgsqlDbType.Varchar, visitorKey);
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
    }
}
