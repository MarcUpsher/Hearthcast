using Hearthcast.Data.Budgets;
using Hearthcast.Data.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class BudgetSnapshotConfiguration : IEntityTypeConfiguration<BudgetSnapshot>
{
    public void Configure(EntityTypeBuilder<BudgetSnapshot> builder)
    {
        builder.ToTable("BudgetSnapshots", table =>
        {
            table.HasCheckConstraint("CK_BudgetSnapshots_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_BudgetSnapshots_EffectiveFrom", "\"EffectiveFrom\" > DATE '0001-01-01' AND \"EffectiveFrom\" < DATE '9999-12-31'");
            table.HasCheckConstraint("CK_BudgetSnapshots_Notes", $"\"Notes\" IS NULL OR ({SpendingSetupConfigurationRules.CanonicalText("Notes")})");
            table.HasCheckConstraint("CK_BudgetSnapshots_CreationHash", "\"CreationRequestHash\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_BudgetSnapshots_Revision", "\"Revision\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_BudgetSnapshots_Timestamps", "isfinite(\"CreatedAtUtc\") AND isfinite(\"UpdatedAtUtc\") AND \"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
            table.HasCheckConstraint("CK_BudgetSnapshots_Source", "\"CopiedFromBudgetSnapshotId\" IS NULL OR \"CopiedFromBudgetSnapshotId\" <> \"Id\"");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasAlternateKey(s => new { s.HouseholdId, s.Id });
        builder.HasIndex(s => new { s.HouseholdId, s.EffectiveFrom }).IsUnique();
        builder.Property(s => s.CreationRequestHash).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Revision).IsConcurrencyToken();
        builder.HasOne<Household>().WithMany().HasForeignKey(s => s.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BudgetSnapshot>().WithMany().HasForeignKey(s => new { s.HouseholdId, s.CopiedFromBudgetSnapshotId })
            .HasPrincipalKey(s => new { s.HouseholdId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(s => s.IsComplete);
    }
}
