using Hearthcast.Data.Classification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories", table =>
        {
            table.HasCheckConstraint("CK_Categories_Name_NotBlank", "length(trim(\"Name\")) > 0");
            table.HasCheckConstraint("CK_Categories_FinancialType", "\"FinancialType\" IN (1, 2)");
        });
        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();
        builder.HasAlternateKey(category => new { category.HouseholdId, category.Id });
        builder.Property(category => category.Name).IsRequired();
        builder.Property(category => category.FinancialType).IsRequired();

        builder.HasOne(category => category.Household)
            .WithMany()
            .HasForeignKey(category => category.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
