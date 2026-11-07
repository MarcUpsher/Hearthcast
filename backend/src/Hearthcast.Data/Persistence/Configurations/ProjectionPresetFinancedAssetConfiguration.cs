using Hearthcast.Data.Projections;
using Hearthcast.Data.Households;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ProjectionPresetFinancedAssetConfiguration : IEntityTypeConfiguration<ProjectionPresetFinancedAsset>
{
    public void Configure(EntityTypeBuilder<ProjectionPresetFinancedAsset> builder)
    {
        builder.ToTable("ProjectionPresetFinancedAssets", table =>
        {
            table.HasCheckConstraint("CK_ProjectionPresetFinancedAssets_MonthlyLoanPaymentInAssetCurrency", "\"MonthlyLoanPaymentInAssetCurrency\" >= 0 AND \"MonthlyLoanPaymentInAssetCurrency\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_ProjectionPresetFinancedAssets_ExtraPayment", "\"ExtraMonthlyLoanPaymentInAssetCurrency\" >= 0 AND \"ExtraMonthlyLoanPaymentInAssetCurrency\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_ProjectionPresetFinancedAssets_InvestmentCurrency", "\"InvestmentCurrencyAfterPayoff\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_ProjectionPresetFinancedAssets_AnnualLoanInterestPercent", "\"AnnualLoanInterestPercent\" BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_ProjectionPresetFinancedAssets_AnnualAssetAppreciationPercent", "\"AnnualAssetAppreciationPercent\" > -100 AND \"AnnualAssetAppreciationPercent\" <= 100");
        });
        builder.HasKey(p => new { p.ProjectionPresetId, p.FinancedAssetId });
        builder.HasOne<ProjectionPreset>().WithMany(p => p.FinancedAssets)
            .HasForeignKey(p => new { p.HouseholdId, p.ProjectionPresetId })
            .HasPrincipalKey(p => new { p.HouseholdId, p.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinancedAsset>().WithMany()
            .HasForeignKey(p => new { p.HouseholdId, p.FinancedAssetId })
            .HasPrincipalKey(p => new { p.HouseholdId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.MonthlyLoanPaymentInAssetCurrency).HasColumnType("numeric");
        builder.HasOne<ProjectionPresetCurrency>().WithMany()
            .HasForeignKey(p => new { p.ProjectionPresetId, p.InvestmentCurrencyAfterPayoff })
            .HasPrincipalKey(c => new { c.ProjectionPresetId, c.CurrencyCode }).OnDelete(DeleteBehavior.NoAction);
        builder.Property(p => p.ExtraMonthlyLoanPaymentInAssetCurrency).HasColumnType("numeric");
        builder.Property(p => p.InvestmentCurrencyAfterPayoff).IsRequired();
        builder.Property(p => p.AnnualLoanInterestPercent).HasColumnType("numeric");
        builder.Property(p => p.AnnualAssetAppreciationPercent).HasColumnType("numeric");
    }
}
