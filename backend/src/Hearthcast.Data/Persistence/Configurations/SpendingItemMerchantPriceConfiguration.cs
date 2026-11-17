using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SpendingItemMerchantPriceConfiguration : IEntityTypeConfiguration<SpendingItemMerchantPrice>
{
    public void Configure(EntityTypeBuilder<SpendingItemMerchantPrice> builder)
    {
        builder.ToTable("SpendingItemMerchantPrices", table =>
        {
            table.HasCheckConstraint("CK_SpendingItemMerchantPrices_CurrencyCode", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_SpendingItemMerchantPrices_UnitKey", "\"UnitKey\" ~ '^[a-z0-9][a-z0-9._-]*$'");
            table.HasCheckConstraint("CK_SpendingItemMerchantPrices_UnitLabel", SpendingSetupConfigurationRules.CanonicalText("UnitLabel"));
            table.HasCheckConstraint("CK_SpendingItemMerchantPrices_UsualUnitPrice",
                "\"UsualUnitPrice\" >= 0 AND \"UsualUnitPrice\" < 1000000000 AND \"UsualUnitPrice\" = round(\"UsualUnitPrice\", 6)");
            table.HasCheckConstraint("CK_SpendingItemMerchantPrices_Revision", "\"Revision\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_SpendingItemMerchantPrices_UpdatedAtUtc", "isfinite(\"UpdatedAtUtc\")");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasAlternateKey(p => new { p.HouseholdId, p.Id });
        builder.HasIndex(p => new { p.HouseholdId, p.SpendingItemId, p.MerchantId, p.CurrencyCode, p.UnitKey })
            .HasDatabaseName(SpendingSetupConfigurationRules.UsualPriceIndex).IsUnique();
        builder.Property(p => p.CurrencyCode).IsRequired();
        builder.Property(p => p.UnitKey).HasMaxLength(SpendingSetupRules.MaximumUnitKeyLength).IsRequired();
        builder.Property(p => p.UnitLabel).IsRequired();
        builder.Property(p => p.UsualUnitPrice).HasColumnType("numeric");
        builder.Property(p => p.Revision).IsConcurrencyToken();
        builder.HasOne<SpendingItem>().WithMany()
            .HasForeignKey(p => new { p.HouseholdId, p.SpendingItemId, p.UnitKey })
            .HasPrincipalKey(i => new { i.HouseholdId, i.Id, i.UnitKey }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Merchant>().WithMany()
            .HasForeignKey(p => new { p.HouseholdId, p.MerchantId })
            .HasPrincipalKey(m => new { m.HouseholdId, m.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpendingLine>().WithMany()
            .HasForeignKey(p => new { p.HouseholdId, p.SourceSpendingLineId })
            .HasPrincipalKey(l => new { l.HouseholdId, l.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
