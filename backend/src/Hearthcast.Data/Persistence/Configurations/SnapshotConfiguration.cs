using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SnapshotConfiguration : IEntityTypeConfiguration<Snapshot>
{
    public void Configure(EntityTypeBuilder<Snapshot> builder)
    {
        builder.ToTable("Snapshots", table =>
        {
            table.HasCheckConstraint("CK_Snapshots_HouseholdName_NotBlank", "length(trim(\"HouseholdName\")) > 0");
            table.HasCheckConstraint("CK_Snapshots_DatePrecision", "\"DatePrecision\" IN (1, 2)");
        });
        builder.Ignore(snapshot => snapshot.Currencies);
        builder.Ignore(snapshot => snapshot.MissingExchangeRateCurrencies);
        builder.Ignore(snapshot => snapshot.ExchangeRateStatus);
        builder.Property(snapshot => snapshot.ExchangeRateBaseCurrency).HasMaxLength(3);
        builder.Navigation(snapshot => snapshot.ExchangeRates).HasField("_exchangeRates")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasKey(snapshot => snapshot.Id);
        builder.Property(snapshot => snapshot.Id).ValueGeneratedNever();
        builder.HasAlternateKey(snapshot => new { snapshot.HouseholdId, snapshot.Id });
        builder.Property(snapshot => snapshot.HouseholdName).IsRequired();
        builder.Property(snapshot => snapshot.Revision).HasDefaultValueSql("gen_random_uuid()").IsConcurrencyToken();
        builder.HasIndex(snapshot => new { snapshot.HouseholdId, snapshot.SnapshotDate });
        builder.HasOne<Household>().WithMany().HasForeignKey(snapshot => snapshot.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(snapshot => snapshot.People)
            .HasField("_people").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(snapshot => snapshot.FinancedAssetSnapshots)
            .HasField("_financedAssetSnapshots").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(snapshot => snapshot.ItemSnapshots)
            .HasField("_itemSnapshots").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
