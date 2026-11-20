using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SpendingLineConfiguration : IEntityTypeConfiguration<SpendingLine>
{
    public void Configure(EntityTypeBuilder<SpendingLine> builder)
    {
        builder.ToTable("SpendingLines", table =>
        {
            table.HasCheckConstraint("CK_SpendingLines_AdditionalCost", """
                NOT "IsAdditionalCost" OR ("NetAmount" > 0 AND "Description" IS NOT NULL
                    AND "SpendingItemId" IS NULL AND "UnitKey" IS NULL AND "UnitLabel" IS NULL
                    AND "Quantity" IS NULL AND "UnitPrice" IS NULL)
                """);
            table.HasCheckConstraint("CK_SpendingLines_Position", "\"Position\" > 0");
            table.HasCheckConstraint("CK_SpendingLines_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$' AND \"CurrencyMinorUnits\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_SpendingLines_NetAmount", "\"NetAmount\" >= 0 AND \"NetAmount\" < 1000000000000 AND \"NetAmount\" = round(\"NetAmount\", \"CurrencyMinorUnits\")");
            table.HasCheckConstraint("CK_SpendingLines_Quantity", "\"Quantity\" IS NULL OR (\"Quantity\" > 0 AND \"Quantity\" < 1000000 AND \"Quantity\" = round(\"Quantity\", 6))");
            table.HasCheckConstraint("CK_SpendingLines_UnitPrice", "\"UnitPrice\" IS NULL OR (\"UnitPrice\" >= 0 AND \"UnitPrice\" < 1000000000 AND \"UnitPrice\" = round(\"UnitPrice\", 6))");
            table.HasCheckConstraint("CK_SpendingLines_Units", """
                (("Quantity" IS NULL AND "UnitPrice" IS NULL)
                    OR ("Quantity" IS NOT NULL AND "UnitPrice" IS NOT NULL AND "UnitKey" IS NOT NULL))
                AND (("UnitKey" IS NULL AND "UnitLabel" IS NULL) OR ("UnitKey" IS NOT NULL AND "UnitLabel" IS NOT NULL))
                """);
            table.HasCheckConstraint("CK_SpendingLines_Item", """
                ("SpendingItemId" IS NULL AND "ItemCode" IS NULL AND "ItemName" IS NULL)
                OR ("SpendingItemId" IS NOT NULL AND "ItemCode" IS NOT NULL AND "ItemName" IS NOT NULL
                    AND "UnitKey" IS NOT NULL AND "UnitLabel" IS NOT NULL)
                """);
            table.HasCheckConstraint("CK_SpendingLines_ItemCode", "\"ItemCode\" IS NULL OR \"ItemCode\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
            table.HasCheckConstraint("CK_SpendingLines_UnitKey", "\"UnitKey\" IS NULL OR \"UnitKey\" ~ '^[a-z0-9][a-z0-9._-]*$'");
            table.HasCheckConstraint("CK_SpendingLines_Promotion", "\"PromotionStatus\" IN (1, 2, 3)");
            table.HasCheckConstraint("CK_SpendingLines_CategoryName", SpendingSetupConfigurationRules.CanonicalText("ExpenseCategoryName"));
            foreach (var column in new[] { "ItemName", "UnitLabel", "Description", "ReceiptDescription", "MerchantItemReference", "Notes" })
                table.HasCheckConstraint($"CK_SpendingLines_{column}", $"\"{column}\" IS NULL OR ({SpendingSetupConfigurationRules.CanonicalText(column)})");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.IsAdditionalCost).HasDefaultValue(false);
        builder.HasAlternateKey(l => new { l.HouseholdId, l.Id });
        builder.HasIndex(l => new { l.SpendingRecordId, l.Position }).IsUnique().HasDatabaseName("UX_SpendingLines_RecordPosition");
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.ExpenseCategoryName).IsRequired();
        builder.Property(l => l.UnitKey).HasMaxLength(SpendingSetupRules.MaximumUnitKeyLength);
        builder.Property(l => l.ItemCode).HasMaxLength(SpendingSetupRules.MaximumCodeLength);
        builder.Property(l => l.NetAmount).HasColumnType("numeric");
        builder.Property(l => l.Quantity).HasColumnType("numeric");
        builder.Property(l => l.UnitPrice).HasColumnType("numeric");
        builder.HasOne<SpendingRecord>().WithMany(r => r.Lines)
            .HasForeignKey(l => new { l.HouseholdId, l.SpendingRecordId, l.CurrencyCode, l.CurrencyMinorUnits })
            .HasPrincipalKey(r => new { r.HouseholdId, r.Id, r.CurrencyCode, r.CurrencyMinorUnits }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(l => new { l.HouseholdId, l.ExpenseCategoryId })
            .HasPrincipalKey(c => new { c.HouseholdId, c.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpendingItem>().WithMany().HasForeignKey(l => new { l.HouseholdId, l.SpendingItemId, l.UnitKey })
            .HasPrincipalKey(i => new { i.HouseholdId, i.Id, i.UnitKey }).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(l => l.SuggestedAmount);
        builder.Ignore(l => l.EffectiveUnitPrice);
    }
}
