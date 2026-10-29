using Hearthcast.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Infrastructure;

public sealed class SqliteTestDatabase : IDbContextFactory<HearthcastDbContext>, IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "hearthcast-tests", Guid.NewGuid().ToString("N"));
    private readonly DbContextOptions<HearthcastDbContext> _options;

    public SqliteTestDatabase()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "test.db"),
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString();
        _options = new DbContextOptionsBuilder<HearthcastDbContext>()
            .UseSqlite(connectionString).Options;
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    public HearthcastDbContext CreateDbContext()
    {
        return new HearthcastDbContext(_options);
    }
}
