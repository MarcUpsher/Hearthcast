using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.TrackedItems;
using Hearthcast.Data.Snapshots;
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

    public DbSet<FinancedAsset> FinancedAssets => Set<FinancedAsset>();
    public DbSet<FinancedAssetOwner> FinancedAssetOwners => Set<FinancedAssetOwner>();
    public DbSet<FinancedAssetLoanHolder> FinancedAssetLoanHolders => Set<FinancedAssetLoanHolder>();
    public DbSet<FinancedAssetHouseholdAllocation> FinancedAssetHouseholdAllocations => Set<FinancedAssetHouseholdAllocation>();

    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<SnapshotPerson> SnapshotPeople => Set<SnapshotPerson>();
    public DbSet<ItemSnapshot> ItemSnapshots => Set<ItemSnapshot>();
    public DbSet<ItemSnapshotHouseholdAllocation> ItemSnapshotHouseholdAllocations => Set<ItemSnapshotHouseholdAllocation>();

    public DbSet<FinancedAssetSnapshot> FinancedAssetSnapshots => Set<FinancedAssetSnapshot>();
    public DbSet<FinancedAssetSnapshotOwner> FinancedAssetSnapshotOwners => Set<FinancedAssetSnapshotOwner>();
    public DbSet<FinancedAssetSnapshotLoanHolder> FinancedAssetSnapshotLoanHolders => Set<FinancedAssetSnapshotLoanHolder>();
    public DbSet<FinancedAssetSnapshotHouseholdAllocation> FinancedAssetSnapshotHouseholdAllocations => Set<FinancedAssetSnapshotHouseholdAllocation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HearthcastDbContext).Assembly);
    }
}
