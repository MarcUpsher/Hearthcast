using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class TrackedItemConfiguration : IEntityTypeConfiguration<TrackedItem>
{
    public void Configure(EntityTypeBuilder<TrackedItem> builder)
    {
        builder.ToTable("TrackedItems", table =>
        {
            table.HasCheckConstraint("CK_TrackedItems_Name_NotBlank", "length(trim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_TrackedItems_CurrencyCode", "\"CurrencyCode\" GLOB '[A-Z][A-Z][A-Z]'");
            table.HasCheckConstraint("CK_TrackedItems_CountryCode", "\"CountryCode\" GLOB '[A-Z][A-Z]'");
            table.HasCheckConstraint("CK_TrackedItems_Inclusion_Boolean", "\"IncludeInHousehold\" IN (0, 1)");
            table.HasCheckConstraint("CK_TrackedItems_Retirement_Boolean", "\"IsRetired\" IN (0, 1)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.HasAlternateKey(item => new { item.HouseholdId, item.Id });
        builder.Property(item => item.Name).IsRequired();
        builder.Property(item => item.CurrencyCode).IsRequired();
        builder.Property(item => item.CountryCode).IsRequired();

        builder.HasOne(item => item.Group).WithMany()
            .HasForeignKey(item => new { item.HouseholdId, item.GroupId })
            .HasPrincipalKey(group => new { group.HouseholdId, group.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.HolderPerson).WithMany()
            .HasForeignKey(item => new { item.HouseholdId, item.HolderPersonId })
            .HasPrincipalKey(person => new { person.HouseholdId, person.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(item => item.HouseholdAllocations)
            .HasField("_householdAllocations").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
