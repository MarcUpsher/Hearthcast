using Hearthcast.Data.Projections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ProjectionPresetCurrencyConfiguration : IEntityTypeConfiguration<ProjectionPresetCurrency>
{
    public void Configure(EntityTypeBuilder<ProjectionPresetCurrency> builder)
    {
        builder.ToTable("ProjectionPresetCurrencies", table =>
        {
            table.HasCheckConstraint("CK_ProjectionPresetCurrencies_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_ProjectionPresetCurrencies_MonthlyInvestmentContribution", "\"MonthlyInvestmentContribution\" >= 0 AND \"MonthlyInvestmentContribution\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_ProjectionPresetCurrencies_AnnualInvestmentReturnPercent", "\"AnnualInvestmentReturnPercent\" > -100 AND \"AnnualInvestmentReturnPercent\" <= 100");
            table.HasCheckConstraint("CK_ProjectionPresetCurrencies_MonthlySavingsContribution", "\"MonthlySavingsContribution\" >= 0 AND \"MonthlySavingsContribution\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_ProjectionPresetCurrencies_AnnualSavingsInterestPercent", "\"AnnualSavingsInterestPercent\" > -100 AND \"AnnualSavingsInterestPercent\" <= 100");
        });
        builder.HasKey(c => new { c.ProjectionPresetId, c.CurrencyCode });
        builder.HasOne<ProjectionPreset>().WithMany(p => p.Currencies).HasForeignKey(c => c.ProjectionPresetId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(c => c.MonthlyInvestmentContribution).HasColumnType("numeric");
        builder.Property(c => c.AnnualInvestmentReturnPercent).HasColumnType("numeric");
        builder.Property(c => c.MonthlySavingsContribution).HasColumnType("numeric");
        builder.Property(c => c.AnnualSavingsInterestPercent).HasColumnType("numeric");
    }
}
