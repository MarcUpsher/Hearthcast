using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class FinancedAssetSnapshotHouseholdAllocationConfiguration : IEntityTypeConfiguration<FinancedAssetSnapshotHouseholdAllocation>
{
    public void Configure(EntityTypeBuilder<FinancedAssetSnapshotHouseholdAllocation> builder)
    {
        builder.ToTable("FinancedAssetSnapshotHouseholdAllocations", table => table.HasCheckConstraint(
            "CK_FinancedAssetSnapshotHouseholdAllocations_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000"));
        builder.HasKey(row => new { row.FinancedAssetSnapshotId, row.SnapshotPersonId });
        builder.HasOne<FinancedAssetSnapshot>().WithMany(asset => asset.HouseholdAllocations)
            .HasForeignKey(row => new { row.SnapshotId, row.FinancedAssetSnapshotId })
            .HasPrincipalKey(asset => new { asset.SnapshotId, asset.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(row => row.SnapshotPerson).WithMany()
            .HasForeignKey(row => new { row.SnapshotId, row.SnapshotPersonId })
            .HasPrincipalKey(person => new { person.SnapshotId, person.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
