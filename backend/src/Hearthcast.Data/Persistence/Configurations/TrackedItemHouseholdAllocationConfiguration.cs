using Hearthcast.Data.TrackedItems;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class TrackedItemHouseholdAllocationConfiguration : IEntityTypeConfiguration<TrackedItemHouseholdAllocation>
{
    public void Configure(EntityTypeBuilder<TrackedItemHouseholdAllocation> builder)
    {
        builder.ToTable("TrackedItemHouseholdAllocations", table =>
            table.HasCheckConstraint("CK_TrackedItemHouseholdAllocations_Share",
                FormattableString.Invariant($"\"ShareBasisPoints\" BETWEEN 1 AND {AllocationShareRules.FullShareBasisPoints}")));
        builder.HasKey(allocation => new { allocation.TrackedItemId, allocation.PersonId });

        builder.HasOne(allocation => allocation.TrackedItem)
            .WithMany(item => item.HouseholdAllocations)
            .HasForeignKey(allocation => new { allocation.HouseholdId, allocation.TrackedItemId })
            .HasPrincipalKey(item => new { item.HouseholdId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.Person).WithMany()
            .HasForeignKey(allocation => new { allocation.HouseholdId, allocation.PersonId })
            .HasPrincipalKey(person => new { person.HouseholdId, person.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
