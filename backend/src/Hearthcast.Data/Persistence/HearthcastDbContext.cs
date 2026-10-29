using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Persistence;

public sealed class HearthcastDbContext(DbContextOptions<HearthcastDbContext> options) : DbContext(options)
{
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ItemGroup> ItemGroups => Set<ItemGroup>();
    public DbSet<TrackedItem> TrackedItems => Set<TrackedItem>();
    public DbSet<TrackedItemHouseholdAllocation> TrackedItemHouseholdAllocations => Set<TrackedItemHouseholdAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HearthcastDbContext).Assembly);
    }
}
