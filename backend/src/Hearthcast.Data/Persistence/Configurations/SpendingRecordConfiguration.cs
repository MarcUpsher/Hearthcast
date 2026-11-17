using Hearthcast.Data.Spending;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SpendingRecordConfiguration : IEntityTypeConfiguration<SpendingRecord>
{
    public void Configure(EntityTypeBuilder<SpendingRecord> builder)
    {
        builder.ToTable("SpendingRecords", table =>
        {
            table.HasCheckConstraint("CK_SpendingRecords_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_SpendingRecords_Kind", "\"Kind\" IN (1, 2)");
            table.HasCheckConstraint("CK_SpendingRecords_Date", "\"SpendingDate\" > DATE '0001-01-01' AND \"SpendingDate\" <= DATE '9999-12-31'");
            table.HasCheckConstraint("CK_SpendingRecords_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$' AND \"CurrencyMinorUnits\" BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_SpendingRecords_MerchantName", SpendingSetupConfigurationRules.CanonicalText("MerchantName"));
            foreach (var column in new[] { "ExternalReference", "Notes" })
                table.HasCheckConstraint($"CK_SpendingRecords_{column}", $"\"{column}\" IS NULL OR ({SpendingSetupConfigurationRules.CanonicalText(column)})");
            table.HasCheckConstraint("CK_SpendingRecords_ReceiptTotal", "\"ReceiptTotal\" IS NULL OR (\"ReceiptTotal\" >= 0 AND \"ReceiptTotal\" < 1000000000000 AND \"ReceiptTotal\" = round(\"ReceiptTotal\", \"CurrencyMinorUnits\"))");
            table.HasCheckConstraint("CK_SpendingRecords_CreationHash", "\"CreationRequestHash\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_SpendingRecords_Revision", "\"Revision\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_SpendingRecords_Timestamps", "isfinite(\"CreatedAtUtc\") AND isfinite(\"UpdatedAtUtc\") AND \"UpdatedAtUtc\" >= \"CreatedAtUtc\" AND (\"VoidedAtUtc\" IS NULL OR (isfinite(\"VoidedAtUtc\") AND \"VoidedAtUtc\" >= \"CreatedAtUtc\" AND \"VoidedAtUtc\" <= \"UpdatedAtUtc\"))");
            table.HasCheckConstraint("CK_SpendingRecords_OriginalPayment", """
                ("OriginalSpendingRecordId" IS NULL AND "OriginalSpendingRecordKind" IS NULL)
                OR ("Kind" = 2 AND "OriginalSpendingRecordId" IS NOT NULL AND "OriginalSpendingRecordKind" IS NOT NULL
                    AND "OriginalSpendingRecordKind" = 1 AND "OriginalSpendingRecordId" <> "Id")
                """);
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasAlternateKey(r => new { r.HouseholdId, r.Id });
        builder.HasAlternateKey(r => new { r.HouseholdId, r.Id, r.Kind });
        builder.HasAlternateKey(r => new { r.HouseholdId, r.Id, r.CurrencyCode, r.CurrencyMinorUnits });
        builder.Property(r => r.CreationRequestHash).HasMaxLength(64).IsRequired();
        builder.Property(r => r.MerchantName).IsRequired();
        builder.Property(r => r.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(r => r.ReceiptTotal).HasColumnType("numeric");
        builder.Property(r => r.Revision).IsConcurrencyToken();
        builder.HasOne<Merchant>().WithMany().HasForeignKey(r => new { r.HouseholdId, r.MerchantId })
            .HasPrincipalKey(m => new { m.HouseholdId, m.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SpendingRecord>().WithMany()
            .HasForeignKey(r => new { r.HouseholdId, r.OriginalSpendingRecordId, r.OriginalSpendingRecordKind })
            .HasPrincipalKey(r => new { r.HouseholdId, r.Id, r.Kind }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.HouseholdId, r.SpendingDate });
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(r => r.IsVoided);
        builder.Ignore(r => r.TotalAmount);
        builder.Ignore(r => r.CountedAmount);
        builder.Ignore(r => r.ReceiptDifference);
        builder.Ignore(r => r.ReconciliationStatus);
    }
}
