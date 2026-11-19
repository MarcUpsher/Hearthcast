using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SpendingDiscountConfiguration : IEntityTypeConfiguration<SpendingDiscount>
{
    public void Configure(EntityTypeBuilder<SpendingDiscount> builder)
    {
        builder.ToTable("SpendingDiscounts", table =>
        {
            table.HasCheckConstraint("CK_SpendingDiscounts_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_SpendingDiscounts_Position", "\"Position\" > 0");
            table.HasCheckConstraint("CK_SpendingDiscounts_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$' AND \"CurrencyMinorUnits\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_SpendingDiscounts_Amount", "\"Amount\" > 0 AND \"Amount\" < 1000000000000 AND \"Amount\" = round(\"Amount\", \"CurrencyMinorUnits\")");
            table.HasCheckConstraint("CK_SpendingDiscounts_CategoryName", SpendingSetupConfigurationRules.CanonicalText("ExpenseCategoryName"));
            table.HasCheckConstraint("CK_SpendingDiscounts_Description", $"\"Description\" IS NULL OR ({SpendingSetupConfigurationRules.CanonicalText("Description")})");
        });
        builder.HasKey(d => d.Id); builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(d => d.ExpenseCategoryName).IsRequired(); builder.Property(d => d.Amount).HasColumnType("numeric");
        builder.HasIndex(d => new { d.SpendingRecordId, d.Position }).IsUnique();
        builder.HasOne<SpendingRecord>().WithMany(r => r.Discounts)
            .HasForeignKey(d => new { d.HouseholdId, d.SpendingRecordId, d.CurrencyCode, d.CurrencyMinorUnits })
            .HasPrincipalKey(r => new { r.HouseholdId, r.Id, r.CurrencyCode, r.CurrencyMinorUnits }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(d => new { d.HouseholdId, d.ExpenseCategoryId })
            .HasPrincipalKey(c => new { c.HouseholdId, c.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
