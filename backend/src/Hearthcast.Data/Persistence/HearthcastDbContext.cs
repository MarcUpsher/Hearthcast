using Hearthcast.Data.Households;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Persistence;

public sealed class HearthcastDbContext(DbContextOptions<HearthcastDbContext> options) : DbContext(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HearthcastDbContext).Assembly);
    }
}
