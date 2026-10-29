using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hearthcast.Data.Persistence.DesignTime;

public sealed class HearthcastDbContextFactory : IDesignTimeDbContextFactory<HearthcastDbContext>
{
    public HearthcastDbContext CreateDbContext(string[] args)
    {
        // Generating migrations needs provider metadata, not a live connection.
        // Applying migrations requires an explicit --connection or environment value.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Hearthcast")
            ?? "Host=localhost;Database=hearthcast;Username=hearthcast";
        var options = new DbContextOptionsBuilder<HearthcastDbContext>()
            .UseNpgsql(connectionString).Options;
        return new HearthcastDbContext(options);
    }
}
