using Hearthcast.Data.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class ItemGroupConfiguration : IEntityTypeConfiguration<ItemGroup>
{
    public void Configure(EntityTypeBuilder<ItemGroup> builder)
    {
        builder.ToTable("ItemGroups", table =>
            table.HasCheckConstraint("CK_ItemGroups_Name_NotBlank", "length(trim(\"Name\")) > 0"));
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Id).ValueGeneratedNever();
        builder.HasAlternateKey(group => new { group.HouseholdId, group.Id });
        builder.Property(group => group.Name).IsRequired();

        builder.HasOne(group => group.Category)
            .WithMany()
            .HasForeignKey(group => new { group.HouseholdId, group.CategoryId })
            .HasPrincipalKey(category => new { category.HouseholdId, category.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
