using Hearthcast.Data.Classification;
using Hearthcast.Data.Persistence.Conversions;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ItemSnapshotConfiguration : IEntityTypeConfiguration<ItemSnapshot>
{
    public void Configure(EntityTypeBuilder<ItemSnapshot> builder)
    {
        builder.ToTable("ItemSnapshots", table =>
        {
            table.HasCheckConstraint("CK_ItemSnapshots_Names_NotBlank",
                "length(trim(\"ItemName\")) > 0 AND length(trim(\"GroupName\")) > 0 AND length(trim(\"CategoryName\")) > 0");
            table.HasCheckConstraint("CK_ItemSnapshots_FinancialType", "\"FinancialType\" IN (1, 2)");
            table.HasCheckConstraint("CK_ItemSnapshots_CurrencyCode", "\"CurrencyCode\" GLOB '[A-Z][A-Z][A-Z]'");
            table.HasCheckConstraint("CK_ItemSnapshots_CountryCode", "\"CountryCode\" GLOB '[A-Z][A-Z]'");
            table.HasCheckConstraint("CK_ItemSnapshots_Inclusion_Boolean", "\"IncludeInHousehold\" IN (0, 1)");
            table.HasCheckConstraint("CK_ItemSnapshots_ValueState", """
                ("Amount" IS NULL AND "ValueOrigin" = 1
                    AND "LastConfirmedDate" IS NULL AND "LastConfirmedPrecision" IS NULL)
                OR ("Amount" IS NOT NULL AND "ValueOrigin" = 2
                    AND "LastConfirmedDate" IS NOT NULL AND "LastConfirmedPrecision" IS NOT NULL
                    AND "LastConfirmedPrecision" IN (1, 2))
                """);
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.HasAlternateKey(item => new { item.SnapshotId, item.Id });
        builder.HasIndex(item => new { item.SnapshotId, item.TrackedItemId }).IsUnique();
        builder.Property(item => item.ItemName).IsRequired();
        builder.Property(item => item.CategoryName).IsRequired();
        builder.Property(item => item.GroupName).IsRequired();
        builder.Property(item => item.CurrencyCode).IsRequired();
        builder.Property(item => item.CountryCode).IsRequired();
        builder.Property(item => item.Amount).HasConversion(new DecimalTextConverter()).HasColumnType("TEXT");

        builder.HasOne<Snapshot>().WithMany(snapshot => snapshot.ItemSnapshots)
            .HasForeignKey(item => new { item.HouseholdId, item.SnapshotId })
            .HasPrincipalKey(snapshot => new { snapshot.HouseholdId, snapshot.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TrackedItem>().WithMany()
            .HasForeignKey(item => new { item.HouseholdId, item.TrackedItemId })
            .HasPrincipalKey(item => new { item.HouseholdId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.HolderSnapshotPerson).WithMany()
            .HasForeignKey(item => new { item.SnapshotId, item.HolderSnapshotPersonId })
            .HasPrincipalKey(person => new { person.SnapshotId, person.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(item => new { item.HouseholdId, item.CategoryId })
            .HasPrincipalKey(category => new { category.HouseholdId, category.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemGroup>().WithMany()
            .HasForeignKey(item => new { item.HouseholdId, item.GroupId })
            .HasPrincipalKey(group => new { group.HouseholdId, group.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(item => item.HouseholdAllocations)
            .HasField("_householdAllocations").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
