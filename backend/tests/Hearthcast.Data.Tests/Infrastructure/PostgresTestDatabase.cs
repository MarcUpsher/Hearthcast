using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Tests.Infrastructure;

public sealed class PostgresTestDatabase : IDbContextFactory<HearthcastDbContext>, IAsyncLifetime
{
    private readonly string _databaseName = $"hearthcast_test_{Guid.NewGuid():N}";
    private readonly string _adminConnectionString;
    private readonly DbContextOptions<HearthcastDbContext> _options;

    public PostgresTestDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("HEARTHCAST_TEST_POSTGRES")
            ?? throw new InvalidOperationException(
                "Run tests with Docker Compose, or set HEARTHCAST_TEST_POSTGRES to a PostgreSQL connection with CREATEDB permission.");
        var connection = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres", Pooling = false };
        _adminConnectionString = connection.ConnectionString;
        connection.Database = _databaseName;
        _options = new DbContextOptionsBuilder<HearthcastDbContext>()
            .UseNpgsql(connection.ConnectionString).Options;
    }

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        // Generated identifiers contain only a fixed prefix and hexadecimal digits.
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", admin);
        await command.ExecuteNonQueryAsync();
        try
        {
            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }

    public HearthcastDbContext CreateDbContext() => new(_options);
}
