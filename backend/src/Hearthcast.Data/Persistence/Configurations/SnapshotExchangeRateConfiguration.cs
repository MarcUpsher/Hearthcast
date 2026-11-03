using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SnapshotExchangeRateConfiguration : IEntityTypeConfiguration<SnapshotExchangeRate>
{
    public void Configure(EntityTypeBuilder<SnapshotExchangeRate> builder)
    {
        builder.ToTable("SnapshotExchangeRates", table =>
        {
            table.HasCheckConstraint("CK_SnapshotExchangeRates_Positive", "\"Rate\" > 0");
            table.HasCheckConstraint("CK_SnapshotExchangeRates_DifferentCurrencies", "\"BaseCurrency\" <> \"QuoteCurrency\"");
        });
        builder.HasKey(rate => new { rate.SnapshotId, rate.QuoteCurrency });
        builder.Property(rate => rate.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(rate => rate.QuoteCurrency).HasMaxLength(3).IsRequired();
        builder.Property(rate => rate.Rate).HasPrecision(36, 18);
        builder.Property(rate => rate.Source).HasMaxLength(100).IsRequired();
        builder.HasOne<Snapshot>().WithMany(snapshot => snapshot.ExchangeRates)
            .HasForeignKey(rate => rate.SnapshotId).OnDelete(DeleteBehavior.Cascade);
    }
}
