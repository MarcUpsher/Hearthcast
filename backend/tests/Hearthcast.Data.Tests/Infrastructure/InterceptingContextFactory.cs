using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hearthcast.Data.Tests.Infrastructure;

internal sealed class InterceptingContextFactory : IDbContextFactory<HearthcastDbContext>
{
    private readonly DbContextOptions<HearthcastDbContext> _options;

    public InterceptingContextFactory(PostgresTestDatabase database, params IInterceptor[] interceptors)
    {
        using var context = database.CreateDbContext();
        _options = new DbContextOptionsBuilder<HearthcastDbContext>()
            .UseNpgsql(context.Database.GetConnectionString()).AddInterceptors(interceptors).Options;
    }

    public HearthcastDbContext CreateDbContext() => new(_options);
}
