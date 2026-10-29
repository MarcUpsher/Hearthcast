using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hearthcast.Data.Persistence.DesignTime;

public sealed class HearthcastDbContextFactory : IDesignTimeDbContextFactory<HearthcastDbContext>
{
    public HearthcastDbContext CreateDbContext(string[] args)
    {
        // Migration generation needs a provider, not a persistent database.
        // Supply --connection explicitly when applying a migration to a file.
        var options = new DbContextOptionsBuilder<HearthcastDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=True")
            .Options;
        return new HearthcastDbContext(options);
    }
}
