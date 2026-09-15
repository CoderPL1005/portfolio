using Npgsql;
using Portfolio.IntegrationTests.PortfolioContent;

namespace Portfolio.IntegrationTests.Persistence;

public sealed class RawJobPostingIngestionKeyPostgresTests
{
    private const string ConnectionVariable = "CHAT_QUOTA_TEST_CONNECTION";

    [PostgresFact]
    public async Task Partial_unique_index_allows_nulls_and_rejects_duplicate_ingestion_keys()
    {
        await using var connection = new NpgsqlConnection(TestConnection());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var schema = new NpgsqlCommand("""
            CREATE TEMP TABLE raw_ingestion_key_test (
                id uuid PRIMARY KEY,
                ingestion_key character varying(500) NULL,
                content_hash character varying(64) NOT NULL
            );
            CREATE UNIQUE INDEX uq_raw_job_postings_ingestion_key
            ON raw_ingestion_key_test (ingestion_key)
            WHERE ingestion_key IS NOT NULL;
            """, connection, transaction))
        {
            await schema.ExecuteNonQueryAsync();
        }

        var sameContentHash = new string('a', 64);
        await InsertAsync(connection, transaction, null, sameContentHash);
        await InsertAsync(connection, transaction, null, sameContentHash);
        await InsertAsync(connection, transaction, "telegram:100:1", sameContentHash);
        await InsertAsync(connection, transaction, "telegram:100:2", sameContentHash);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(connection, transaction, "telegram:100:1", new string('b', 64)));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? ingestionKey,
        string contentHash)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO raw_ingestion_key_test (id, ingestion_key, content_hash)
            VALUES (@id, @ingestion_key, @content_hash);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("ingestion_key", (object?)ingestionKey ?? DBNull.Value);
        command.Parameters.AddWithValue("content_hash", contentHash);
        await command.ExecuteNonQueryAsync();
    }

    private static string TestConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} was not configured.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.Database?.EndsWith("_tests", StringComparison.OrdinalIgnoreCase) != true
            || builder.Host is not ("localhost" or "127.0.0.1"))
        {
            throw new InvalidOperationException(
                "Raw job ingestion-key tests require a local database whose name ends in _tests.");
        }

        return builder.ConnectionString;
    }
}
