using Hearthcast.Data.Households;

namespace Hearthcast.Data.Snapshots;

public sealed class SnapshotPerson
{
    private SnapshotPerson()
    {
    }

    internal SnapshotPerson(Snapshot snapshot, Person person)
    {
        Id = Guid.NewGuid();
        HouseholdId = snapshot.HouseholdId;
        SnapshotId = snapshot.Id;
        PersonId = person.Id;
        DisplayName = person.DisplayName;
        ParticipatesInHouseholdFinances = person.ParticipatesInHouseholdFinances;
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid PersonId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public bool ParticipatesInHouseholdFinances { get; private set; }
}
