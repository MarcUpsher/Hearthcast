using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class FinancedAssetSnapshotConfiguration : IEntityTypeConfiguration<FinancedAssetSnapshot>
{
    public void Configure(EntityTypeBuilder<FinancedAssetSnapshot> builder)
    {
        builder.ToTable("FinancedAssetSnapshots", table =>
        {
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_Names", "length(trim(\"AssetName\")) > 0 AND length(trim(\"GroupName\")) > 0 AND length(trim(\"CategoryName\")) > 0");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_FinancialType", "\"FinancialType\" = 1");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_Currency", "\"CurrencyCode\" ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_Country", "\"CountryCode\" ~ '^[A-Z]{2}$'");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_Price", "\"PurchasePrice\" >= 0 AND \"PurchasePrice\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_OriginalLoan", "\"OriginalLoanAmount\" >= 0 AND \"OriginalLoanAmount\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_WithdrawalFacility", "\"HasWithdrawalFacility\" OR (\"AvailableToWithdraw\" IS NULL AND \"AvailableToWithdrawValueOrigin\" = 1)");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_CurrentValuation_Amount", "\"CurrentValuation\" >= 0 AND \"CurrentValuation\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_CurrentValuation_Source", "\"CurrentValuationCarriedFromSnapshotId\" IS NULL OR \"CurrentValuationCarriedFromSnapshotId\" <> \"Id\"");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_CurrentValuation_State", """
                ("CurrentValuation" IS NULL AND "CurrentValuationValueOrigin" = 1 AND "CurrentValuationCarriedFromSnapshotId" IS NULL
                    AND "CurrentValuationLastConfirmedDate" IS NULL AND "CurrentValuationLastConfirmedPrecision" IS NULL)
                OR ("CurrentValuation" IS NOT NULL AND "CurrentValuationValueOrigin" = 2 AND "CurrentValuationCarriedFromSnapshotId" IS NULL
                    AND "CurrentValuationLastConfirmedDate" IS NOT NULL AND "CurrentValuationLastConfirmedPrecision" IS NOT NULL
                    AND "CurrentValuationLastConfirmedPrecision" IN (1, 2))
                OR ("CurrentValuation" IS NOT NULL AND "CurrentValuationValueOrigin" = 3 AND "CurrentValuationCarriedFromSnapshotId" IS NOT NULL
                    AND (("CurrentValuationLastConfirmedDate" IS NULL AND "CurrentValuationLastConfirmedPrecision" IS NULL)
                        OR ("CurrentValuationLastConfirmedDate" IS NOT NULL AND "CurrentValuationLastConfirmedPrecision" IS NOT NULL
                            AND "CurrentValuationLastConfirmedPrecision" IN (1, 2))))
                """);
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_OutstandingLoanBalance_Amount", "\"OutstandingLoanBalance\" >= 0 AND \"OutstandingLoanBalance\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_OutstandingLoanBalance_Source", "\"OutstandingLoanBalanceCarriedFromSnapshotId\" IS NULL OR \"OutstandingLoanBalanceCarriedFromSnapshotId\" <> \"Id\"");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_OutstandingLoanBalance_State", """
                ("OutstandingLoanBalance" IS NULL AND "OutstandingLoanBalanceValueOrigin" = 1 AND "OutstandingLoanBalanceCarriedFromSnapshotId" IS NULL
                    AND "OutstandingLoanBalanceLastConfirmedDate" IS NULL AND "OutstandingLoanBalanceLastConfirmedPrecision" IS NULL)
                OR ("OutstandingLoanBalance" IS NOT NULL AND "OutstandingLoanBalanceValueOrigin" = 2 AND "OutstandingLoanBalanceCarriedFromSnapshotId" IS NULL
                    AND "OutstandingLoanBalanceLastConfirmedDate" IS NOT NULL AND "OutstandingLoanBalanceLastConfirmedPrecision" IS NOT NULL
                    AND "OutstandingLoanBalanceLastConfirmedPrecision" IN (1, 2))
                OR ("OutstandingLoanBalance" IS NOT NULL AND "OutstandingLoanBalanceValueOrigin" = 3 AND "OutstandingLoanBalanceCarriedFromSnapshotId" IS NOT NULL
                    AND (("OutstandingLoanBalanceLastConfirmedDate" IS NULL AND "OutstandingLoanBalanceLastConfirmedPrecision" IS NULL)
                        OR ("OutstandingLoanBalanceLastConfirmedDate" IS NOT NULL AND "OutstandingLoanBalanceLastConfirmedPrecision" IS NOT NULL
                            AND "OutstandingLoanBalanceLastConfirmedPrecision" IN (1, 2))))
                """);
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_AvailableToWithdraw_Amount", "\"AvailableToWithdraw\" >= 0 AND \"AvailableToWithdraw\" < 'Infinity'::numeric");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_AvailableToWithdraw_Source", "\"AvailableToWithdrawCarriedFromSnapshotId\" IS NULL OR \"AvailableToWithdrawCarriedFromSnapshotId\" <> \"Id\"");
            table.HasCheckConstraint("CK_FinancedAssetSnapshots_AvailableToWithdraw_State", """
                ("AvailableToWithdraw" IS NULL AND "AvailableToWithdrawValueOrigin" = 1 AND "AvailableToWithdrawCarriedFromSnapshotId" IS NULL
                    AND "AvailableToWithdrawLastConfirmedDate" IS NULL AND "AvailableToWithdrawLastConfirmedPrecision" IS NULL)
                OR ("AvailableToWithdraw" IS NOT NULL AND "AvailableToWithdrawValueOrigin" = 2 AND "AvailableToWithdrawCarriedFromSnapshotId" IS NULL
                    AND "AvailableToWithdrawLastConfirmedDate" IS NOT NULL AND "AvailableToWithdrawLastConfirmedPrecision" IS NOT NULL
                    AND "AvailableToWithdrawLastConfirmedPrecision" IN (1, 2))
                OR ("AvailableToWithdraw" IS NOT NULL AND "AvailableToWithdrawValueOrigin" = 3 AND "AvailableToWithdrawCarriedFromSnapshotId" IS NOT NULL
                    AND (("AvailableToWithdrawLastConfirmedDate" IS NULL AND "AvailableToWithdrawLastConfirmedPrecision" IS NULL)
                        OR ("AvailableToWithdrawLastConfirmedDate" IS NOT NULL AND "AvailableToWithdrawLastConfirmedPrecision" IS NOT NULL
                            AND "AvailableToWithdrawLastConfirmedPrecision" IN (1, 2))))
                """);
        });
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).ValueGeneratedNever();
        builder.HasAlternateKey(asset => new { asset.SnapshotId, asset.Id });
        builder.HasAlternateKey(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.Id });
        builder.HasIndex(asset => new { asset.SnapshotId, asset.FinancedAssetId }).IsUnique();
        builder.Property(asset => asset.AssetName).IsRequired();
        builder.Property(asset => asset.CategoryName).IsRequired();
        builder.Property(asset => asset.GroupName).IsRequired();
        builder.Property(asset => asset.CurrencyCode).IsRequired();
        builder.Property(asset => asset.CountryCode).IsRequired();
        builder.Property(asset => asset.PurchasePrice).HasColumnType("numeric");
        builder.Property(asset => asset.OriginalLoanAmount).HasColumnType("numeric");
        builder.Ignore(asset => asset.CurrentEquity);
        builder.Ignore(asset => asset.LoanBalanceReduction);
        builder.Ignore(asset => asset.LoanBalanceReductionExcludingWithdrawableFunds);
        builder.Property(asset => asset.CurrentValuation).HasColumnType("numeric");
        builder.HasIndex(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.CurrentValuationCarriedFromSnapshotId })
            .HasDatabaseName("IX_FinancedAssetSnapshots_CurrentValuationSource");
        builder.HasOne<FinancedAssetSnapshot>().WithMany()
            .HasForeignKey(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.CurrentValuationCarriedFromSnapshotId })
            .HasConstraintName("FK_FinancedAssetSnapshots_CurrentValuationSource")
            .HasPrincipalKey(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(asset => asset.OutstandingLoanBalance).HasColumnType("numeric");
        builder.HasIndex(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.OutstandingLoanBalanceCarriedFromSnapshotId })
            .HasDatabaseName("IX_FinancedAssetSnapshots_OutstandingLoanBalanceSource");
        builder.HasOne<FinancedAssetSnapshot>().WithMany()
            .HasForeignKey(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.OutstandingLoanBalanceCarriedFromSnapshotId })
            .HasConstraintName("FK_FinancedAssetSnapshots_OutstandingLoanBalanceSource")
            .HasPrincipalKey(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(asset => asset.AvailableToWithdraw).HasColumnType("numeric");
        builder.HasIndex(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.AvailableToWithdrawCarriedFromSnapshotId })
            .HasDatabaseName("IX_FinancedAssetSnapshots_AvailableToWithdrawSource");
        builder.HasOne<FinancedAssetSnapshot>().WithMany()
            .HasForeignKey(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.AvailableToWithdrawCarriedFromSnapshotId })
            .HasConstraintName("FK_FinancedAssetSnapshots_AvailableToWithdrawSource")
            .HasPrincipalKey(asset => new { asset.FinancedAssetId, asset.CurrencyCode, asset.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Snapshot>().WithMany(snapshot => snapshot.FinancedAssetSnapshots)
            .HasForeignKey(asset => new { asset.HouseholdId, asset.SnapshotId })
            .HasPrincipalKey(snapshot => new { snapshot.HouseholdId, snapshot.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinancedAsset>().WithMany()
            .HasForeignKey(asset => new { asset.HouseholdId, asset.FinancedAssetId })
            .HasPrincipalKey(asset => new { asset.HouseholdId, asset.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(asset => new { asset.HouseholdId, asset.CategoryId })
            .HasPrincipalKey(category => new { category.HouseholdId, category.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemGroup>().WithMany()
            .HasForeignKey(asset => new { asset.HouseholdId, asset.GroupId })
            .HasPrincipalKey(group => new { group.HouseholdId, group.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(asset => asset.Owners).HasField("_owners").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(asset => asset.LoanHolders).HasField("_loanHolders").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(asset => asset.HouseholdAllocations).HasField("_householdAllocations").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
