using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("ExpenseCategories", table =>
            table.HasCheckConstraint("CK_ExpenseCategories_Name", SpendingSetupConfigurationRules.CanonicalText("Name")));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasAlternateKey(c => new { c.HouseholdId, c.Id });
        builder.Property(c => c.Name).IsRequired();
        builder.HasOne<Household>().WithMany().HasForeignKey(c => c.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}
