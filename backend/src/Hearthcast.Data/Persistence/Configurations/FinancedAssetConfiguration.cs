using Hearthcast.Data.FinancedAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class FinancedAssetConfiguration : IEntityTypeConfiguration<FinancedAsset>
{
    public void Configure(EntityTypeBuilder<FinancedAsset> builder)
    {
        builder.ToTable("FinancedAssets", table =>
        {
            table.HasCheckConstraint("CK_FinancedAssets_Name_NotBlank", "length(trim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_FinancedAssets_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_FinancedAssets_CountryCode", "\"CountryCode\" ~ '^[A-Z]{2}$'");
            table.HasCheckConstraint("CK_FinancedAssets_PurchasePrice", "\"PurchasePrice\" >= 0 AND \"PurchasePrice\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_FinancedAssets_OriginalLoanAmount", "\"OriginalLoanAmount\" >= 0 AND \"OriginalLoanAmount\" < 'Infinity'::numeric");
        });
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).ValueGeneratedNever();
        builder.HasAlternateKey(asset => new { asset.HouseholdId, asset.Id });
        builder.Property(asset => asset.Name).IsRequired();
        builder.Property(asset => asset.CurrencyCode).IsRequired();
        builder.Property(asset => asset.CountryCode).IsRequired();
        builder.Property(asset => asset.PurchasePrice).HasColumnType("numeric");
        builder.Property(asset => asset.OriginalLoanAmount).HasColumnType("numeric");
        builder.HasOne(asset => asset.Group).WithMany()
            .HasForeignKey(asset => new { asset.HouseholdId, asset.GroupId })
            .HasPrincipalKey(group => new { group.HouseholdId, group.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(asset => asset.Owners).HasField("_owners").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(asset => asset.LoanHolders).HasField("_loanHolders").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(asset => asset.HouseholdAllocations).HasField("_householdAllocations").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
