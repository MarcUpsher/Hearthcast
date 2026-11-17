using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SpendingItemConfiguration : IEntityTypeConfiguration<SpendingItem>
{
    public void Configure(EntityTypeBuilder<SpendingItem> builder)
    {
        builder.ToTable("SpendingItems", table =>
        {
            table.HasCheckConstraint("CK_SpendingItems_Code", "\"Code\" ~ '^[A-Z0-9][A-Z0-9._-]*$'");
            table.HasCheckConstraint("CK_SpendingItems_UnitKey", "\"UnitKey\" ~ '^[a-z0-9][a-z0-9._-]*$'");
            table.HasCheckConstraint("CK_SpendingItems_Name", SpendingSetupConfigurationRules.CanonicalText("Name"));
            table.HasCheckConstraint("CK_SpendingItems_UnitLabel", SpendingSetupConfigurationRules.CanonicalText("UnitLabel"));
            table.HasCheckConstraint("CK_SpendingItems_Notes", $"\"Notes\" IS NULL OR ({SpendingSetupConfigurationRules.CanonicalText("Notes")})");
        });
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.HasAlternateKey(i => new { i.HouseholdId, i.Id });
        builder.HasAlternateKey(i => new { i.HouseholdId, i.Id, i.UnitKey });
        builder.HasIndex(i => new { i.HouseholdId, i.Code }).IsUnique();
        builder.Property(i => i.Code).HasMaxLength(SpendingSetupRules.MaximumCodeLength).IsRequired();
        builder.Property(i => i.Name).IsRequired();
        builder.Property(i => i.UnitKey).HasMaxLength(SpendingSetupRules.MaximumUnitKeyLength).IsRequired();
        builder.Property(i => i.UnitLabel).IsRequired();
        builder.HasOne<ExpenseCategory>().WithMany()
            .HasForeignKey(i => new { i.HouseholdId, i.ExpenseCategoryId })
            .HasPrincipalKey(c => new { c.HouseholdId, c.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
