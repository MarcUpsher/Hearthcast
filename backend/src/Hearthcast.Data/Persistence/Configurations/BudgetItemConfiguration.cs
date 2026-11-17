using Hearthcast.Data.Budgets;
using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class BudgetItemConfiguration : IEntityTypeConfiguration<BudgetItem>
{
    internal const string CodeIndexName = "IX_BudgetItems_HouseholdId_Code";

    public void Configure(EntityTypeBuilder<BudgetItem> builder)
    {
        builder.ToTable("BudgetItems", table =>
        {
            table.HasCheckConstraint("CK_BudgetItems_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_BudgetItems_Code", "\"Code\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
            table.HasCheckConstraint("CK_BudgetItems_Name", SpendingSetupConfigurationRules.CanonicalText("Name"));
            table.HasCheckConstraint("CK_BudgetItems_Notes", $"\"Notes\" IS NULL OR ({SpendingSetupConfigurationRules.CanonicalText("Notes")})");
            table.HasCheckConstraint("CK_BudgetItems_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_BudgetItems_Recurrence", "\"Frequency\" IN (1, 2, 3) AND \"IntervalCount\" > 0");
        });
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.HasAlternateKey(i => new { i.HouseholdId, i.Id });
        builder.HasIndex(i => new { i.HouseholdId, i.Code }).IsUnique().HasDatabaseName(CodeIndexName);
        builder.Property(i => i.Code).HasMaxLength(SpendingSetupRules.MaximumCodeLength).IsRequired();
        builder.Property(i => i.Name).IsRequired();
        builder.Property(i => i.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(i => new { i.HouseholdId, i.ExpenseCategoryId })
            .HasPrincipalKey(c => new { c.HouseholdId, c.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(i => new { i.HouseholdId, i.MerchantId })
            .HasPrincipalKey(m => new { m.HouseholdId, m.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
