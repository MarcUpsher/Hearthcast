using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hearthcast.Data.Persistence.Configurations;

public sealed class SnapshotPersonConfiguration : IEntityTypeConfiguration<SnapshotPerson>
{
    public void Configure(EntityTypeBuilder<SnapshotPerson> builder)
    {
        builder.ToTable("SnapshotPeople", table =>
        {
            table.HasCheckConstraint("CK_SnapshotPeople_DisplayName_NotBlank", "length(trim(\"DisplayName\")) > 0");
            table.HasCheckConstraint("CK_SnapshotPeople_Participation_Boolean", "\"ParticipatesInHouseholdFinances\" IN (0, 1)");
        });
        builder.HasKey(person => person.Id);
        builder.Property(person => person.Id).ValueGeneratedNever();
        builder.HasAlternateKey(person => new { person.SnapshotId, person.Id });
        builder.HasIndex(person => new { person.SnapshotId, person.PersonId }).IsUnique();
        builder.Property(person => person.DisplayName).IsRequired();
        builder.HasOne<Snapshot>().WithMany(snapshot => snapshot.People)
            .HasForeignKey(person => new { person.HouseholdId, person.SnapshotId })
            .HasPrincipalKey(snapshot => new { snapshot.HouseholdId, snapshot.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany()
            .HasForeignKey(person => new { person.HouseholdId, person.PersonId })
            .HasPrincipalKey(person => new { person.HouseholdId, person.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
