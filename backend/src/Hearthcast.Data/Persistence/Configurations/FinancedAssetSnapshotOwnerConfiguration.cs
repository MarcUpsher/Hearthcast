using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class FinancedAssetSnapshotOwnerConfiguration : IEntityTypeConfiguration<FinancedAssetSnapshotOwner>
{
    public void Configure(EntityTypeBuilder<FinancedAssetSnapshotOwner> builder)
    {
        builder.ToTable("FinancedAssetSnapshotOwners", table => table.HasCheckConstraint(
            "CK_FinancedAssetSnapshotOwners_Share", "\"ShareBasisPoints\" BETWEEN 1 AND 10000"));
        builder.HasKey(row => new { row.FinancedAssetSnapshotId, row.SnapshotPersonId });
        builder.HasOne<FinancedAssetSnapshot>().WithMany(asset => asset.Owners)
            .HasForeignKey(row => new { row.SnapshotId, row.FinancedAssetSnapshotId })
            .HasPrincipalKey(asset => new { asset.SnapshotId, asset.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(row => row.SnapshotPerson).WithMany()
            .HasForeignKey(row => new { row.SnapshotId, row.SnapshotPersonId })
            .HasPrincipalKey(person => new { person.SnapshotId, person.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
