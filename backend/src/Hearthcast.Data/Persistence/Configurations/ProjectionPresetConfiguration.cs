using Hearthcast.Data.Projections;
using Hearthcast.Data.Households;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ProjectionPresetConfiguration : IEntityTypeConfiguration<ProjectionPreset>
{
    public void Configure(EntityTypeBuilder<ProjectionPreset> builder)
    {
        builder.ToTable("ProjectionPresets", table =>
        {
            table.HasCheckConstraint("CK_ProjectionPresets_Name_NotBlank", "length(trim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_ProjectionPresets_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_ProjectionPresets_DurationYears", "\"DurationYears\" BETWEEN 1 AND 100");
            table.HasCheckConstraint("CK_ProjectionPresets_AnnualInflationPercent", "\"AnnualInflationPercent\" > -100 AND \"AnnualInflationPercent\" <= 100");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasAlternateKey(p => new { p.HouseholdId, p.Id });
        builder.HasIndex(p => new { p.HouseholdId, p.Name }).IsUnique();
        builder.Property(p => p.Name).IsRequired();
        builder.Property(p => p.CurrencyCode).IsRequired();
        builder.Property(p => p.AnnualInflationPercent).HasColumnType("numeric");
        builder.HasOne<Household>().WithMany().HasForeignKey(p => p.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Currencies).HasField("_currencies").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Groups).HasField("_groups").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.FinancedAssets).HasField("_financedAssets").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
