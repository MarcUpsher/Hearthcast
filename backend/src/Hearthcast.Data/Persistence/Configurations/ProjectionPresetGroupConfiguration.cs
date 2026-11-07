using Hearthcast.Data.Projections;
using Hearthcast.Data.Households;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ProjectionPresetGroupConfiguration : IEntityTypeConfiguration<ProjectionPresetGroup>
{
    public void Configure(EntityTypeBuilder<ProjectionPresetGroup> builder)
    {
        builder.ToTable("ProjectionPresetGroups", table =>
        {
            table.HasCheckConstraint("CK_ProjectionPresetGroups_Bucket", "\"Bucket\" IN (1, 2)");
        });
        builder.HasKey(p => new { p.ProjectionPresetId, p.GroupId });
        builder.HasOne<ProjectionPreset>().WithMany(p => p.Groups)
            .HasForeignKey(p => new { p.HouseholdId, p.ProjectionPresetId })
            .HasPrincipalKey(p => new { p.HouseholdId, p.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ItemGroup>().WithMany()
            .HasForeignKey(p => new { p.HouseholdId, p.GroupId })
            .HasPrincipalKey(p => new { p.HouseholdId, p.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
