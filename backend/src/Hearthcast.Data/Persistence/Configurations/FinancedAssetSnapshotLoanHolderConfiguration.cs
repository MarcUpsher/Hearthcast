using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class FinancedAssetSnapshotLoanHolderConfiguration : IEntityTypeConfiguration<FinancedAssetSnapshotLoanHolder>
{
    public void Configure(EntityTypeBuilder<FinancedAssetSnapshotLoanHolder> builder)
    {
        builder.ToTable("FinancedAssetSnapshotLoanHolders");
        builder.HasKey(row => new { row.FinancedAssetSnapshotId, row.SnapshotPersonId });
        builder.HasOne<FinancedAssetSnapshot>().WithMany(asset => asset.LoanHolders)
            .HasForeignKey(row => new { row.SnapshotId, row.FinancedAssetSnapshotId })
            .HasPrincipalKey(asset => new { asset.SnapshotId, asset.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(row => row.SnapshotPerson).WithMany()
            .HasForeignKey(row => new { row.SnapshotId, row.SnapshotPersonId })
            .HasPrincipalKey(person => new { person.SnapshotId, person.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
