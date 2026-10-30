using Hearthcast.Data.FinancedAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class FinancedAssetHouseholdAllocationConfiguration : IEntityTypeConfiguration<FinancedAssetHouseholdAllocation>
{
    public void Configure(EntityTypeBuilder<FinancedAssetHouseholdAllocation> builder)
    {
        builder.ToTable("FinancedAssetHouseholdAllocations", table => table.HasCheckConstraint(
            "CK_FinancedAssetHouseholdAllocations_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000"));
        builder.HasKey(row => new { row.FinancedAssetId, row.PersonId });
        builder.HasOne(row => row.FinancedAsset).WithMany(asset => asset.HouseholdAllocations)
            .HasForeignKey(row => new { row.HouseholdId, row.FinancedAssetId })
            .HasPrincipalKey(asset => new { asset.HouseholdId, asset.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(row => row.Person).WithMany()
            .HasForeignKey(row => new { row.HouseholdId, row.PersonId })
            .HasPrincipalKey(person => new { person.HouseholdId, person.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
