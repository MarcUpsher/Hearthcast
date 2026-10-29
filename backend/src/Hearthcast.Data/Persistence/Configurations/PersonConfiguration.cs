using Hearthcast.Data.Households;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People", table =>
        {
            table.HasCheckConstraint("CK_People_DisplayName_NotBlank", "length(trim(\"DisplayName\")) > 0");
        });
        builder.HasKey(person => person.Id);
        builder.Property(person => person.Id).ValueGeneratedNever();
        builder.HasAlternateKey(person => new { person.HouseholdId, person.Id });
        builder.Property(person => person.DisplayName).IsRequired();
        builder.Property(person => person.ParticipatesInHouseholdFinances).IsRequired();
    }
}
