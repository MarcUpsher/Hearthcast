using Hearthcast.Data.Budgets;
using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class BudgetEntryConfiguration : IEntityTypeConfiguration<BudgetEntry>
{
    internal const string AllowanceIndexName = "UX_BudgetEntries_Allowance";

    public void Configure(EntityTypeBuilder<BudgetEntry> builder)
    {
        builder.ToTable("BudgetEntries", table =>
        {
            table.HasCheckConstraint("CK_BudgetEntries_Kind", "\"Kind\" IN (1, 2)");
            table.HasCheckConstraint("CK_BudgetEntries_Allowance", "\"Kind\" <> 2 OR (\"MerchantId\" IS NULL AND \"MerchantName\" IS NULL)");
            table.HasCheckConstraint("CK_BudgetEntries_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_BudgetEntries_ItemCode", "\"ItemCode\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
            foreach (var column in new[] { "ItemName", "ExpenseCategoryName" })
                table.HasCheckConstraint($"CK_BudgetEntries_{column}", SpendingSetupConfigurationRules.CanonicalText(column));
            foreach (var column in new[] { "MerchantName", "Notes" })
                table.HasCheckConstraint($"CK_BudgetEntries_{column}", $"\"{column}\" IS NULL OR ({SpendingSetupConfigurationRules.CanonicalText(column)})");
            table.HasCheckConstraint("CK_BudgetEntries_Merchant", "(\"MerchantId\" IS NULL AND \"MerchantName\" IS NULL) OR (\"MerchantId\" IS NOT NULL AND \"MerchantName\" IS NOT NULL)");
            table.HasCheckConstraint("CK_BudgetEntries_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_BudgetEntries_Recurrence", "\"Frequency\" IN (1, 2, 3) AND \"IntervalCount\" > 0");
            table.HasCheckConstraint("CK_BudgetEntries_Amount", "\"PlannedAmount\" IS NULL OR (\"PlannedAmount\" >= 0 AND \"PlannedAmount\" < 1000000000000 AND \"PlannedAmount\" = round(\"PlannedAmount\", 6))");
            table.HasCheckConstraint("CK_BudgetEntries_ConfirmationDate", "\"LastConfirmedDate\" IS NULL OR (\"LastConfirmedDate\" > DATE '0001-01-01' AND \"LastConfirmedDate\" < DATE '9999-12-31')");
            table.HasCheckConstraint("CK_BudgetEntries_Provenance", """
                ("ValueOrigin" = 1 AND "PlannedAmount" IS NULL AND "LastConfirmedDate" IS NULL AND "CarriedFromBudgetEntryId" IS NULL)
                OR ("ValueOrigin" = 2 AND "PlannedAmount" IS NOT NULL AND "LastConfirmedDate" IS NOT NULL AND "CarriedFromBudgetEntryId" IS NULL)
                OR ("ValueOrigin" = 3 AND "PlannedAmount" IS NOT NULL AND "LastConfirmedDate" IS NOT NULL)
                """);
            table.HasCheckConstraint("CK_BudgetEntries_Source", "\"CarriedFromBudgetEntryId\" IS NULL OR \"CarriedFromBudgetEntryId\" <> \"Id\"");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasDefaultValue(BudgetItemKind.Commitment);
        builder.HasIndex(e => new { e.BudgetSnapshotId, e.ExpenseCategoryId, e.CurrencyCode }).IsUnique()
            .HasFilter("\"Kind\" = 2").HasDatabaseName(AllowanceIndexName);
        builder.HasAlternateKey(e => new { e.HouseholdId, e.Id });
        builder.HasIndex(e => new { e.BudgetSnapshotId, e.BudgetItemId }).IsUnique();
        builder.Property(e => e.ItemCode).HasMaxLength(SpendingSetupRules.MaximumCodeLength).IsRequired();
        builder.Property(e => e.ItemName).IsRequired();
        builder.Property(e => e.ExpenseCategoryName).IsRequired();
        builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(e => e.PlannedAmount).HasColumnType("numeric");
        builder.HasOne<BudgetSnapshot>().WithMany(s => s.Entries).HasForeignKey(e => new { e.HouseholdId, e.BudgetSnapshotId })
            .HasPrincipalKey(s => new { s.HouseholdId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BudgetItem>().WithMany().HasForeignKey(e => new { e.HouseholdId, e.BudgetItemId })
            .HasPrincipalKey(i => new { i.HouseholdId, i.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(e => new { e.HouseholdId, e.ExpenseCategoryId })
            .HasPrincipalKey(c => new { c.HouseholdId, c.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(e => new { e.HouseholdId, e.MerchantId })
            .HasPrincipalKey(m => new { m.HouseholdId, m.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BudgetEntry>().WithMany().HasForeignKey(e => new { e.HouseholdId, e.CarriedFromBudgetEntryId })
            .HasPrincipalKey(e => new { e.HouseholdId, e.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
