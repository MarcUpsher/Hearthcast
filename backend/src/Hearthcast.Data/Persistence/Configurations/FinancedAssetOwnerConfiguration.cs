using Hearthcast.Data.FinancedAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class FinancedAssetOwnerConfiguration : IEntityTypeConfiguration<FinancedAssetOwner>
{
    public void Configure(EntityTypeBuilder<FinancedAssetOwner> builder)
    {
        builder.ToTable("FinancedAssetOwners", table => table.HasCheckConstraint(
            "CK_FinancedAssetOwners_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000"));
        builder.HasKey(row => new { row.FinancedAssetId, row.PersonId });
        builder.HasOne(row => row.FinancedAsset).WithMany(asset => asset.Owners)
            .HasForeignKey(row => new { row.HouseholdId, row.FinancedAssetId })
            .HasPrincipalKey(asset => new { asset.HouseholdId, asset.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(row => row.Person).WithMany()
            .HasForeignKey(row => new { row.HouseholdId, row.PersonId })
            .HasPrincipalKey(person => new { person.HouseholdId, person.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
