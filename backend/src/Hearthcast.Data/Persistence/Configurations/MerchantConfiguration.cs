using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants", table =>
            table.HasCheckConstraint("CK_Merchants_Name", SpendingSetupConfigurationRules.CanonicalText("Name")));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.HasAlternateKey(m => new { m.HouseholdId, m.Id });
        builder.Property(m => m.Name).IsRequired();
        builder.HasOne<Household>().WithMany().HasForeignKey(m => m.HouseholdId).OnDelete(DeleteBehavior.Restrict);
    }
}
