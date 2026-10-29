using Hearthcast.Data.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("Households", table =>
            table.HasCheckConstraint("CK_Households_Name_NotBlank", "length(trim(\"Name\")) > 0"));
        builder.HasKey(household => household.Id);
        builder.Property(household => household.Id).ValueGeneratedNever();
        builder.Property(household => household.Name).IsRequired();

        builder.HasMany(household => household.People)
            .WithOne(person => person.Household)
            .HasForeignKey(person => person.HouseholdId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(household => household.People)
            .HasField("_people")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
