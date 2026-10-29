using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ItemSnapshotHouseholdAllocationConfiguration : IEntityTypeConfiguration<ItemSnapshotHouseholdAllocation>
{
    public void Configure(EntityTypeBuilder<ItemSnapshotHouseholdAllocation> builder)
    {
        builder.ToTable("ItemSnapshotHouseholdAllocations", table =>
            table.HasCheckConstraint("CK_ItemSnapshotHouseholdAllocations_Share",
                FormattableString.Invariant($"\"ShareBasisPoints\" BETWEEN 1 AND {AllocationShareRules.FullShareBasisPoints}")));
        builder.HasKey(allocation => new { allocation.ItemSnapshotId, allocation.SnapshotPersonId });
        builder.HasOne<ItemSnapshot>().WithMany(item => item.HouseholdAllocations)
            .HasForeignKey(allocation => new { allocation.SnapshotId, allocation.ItemSnapshotId })
            .HasPrincipalKey(item => new { item.SnapshotId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.SnapshotPerson).WithMany()
            .HasForeignKey(allocation => new { allocation.SnapshotId, allocation.SnapshotPersonId })
            .HasPrincipalKey(person => new { person.SnapshotId, person.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
