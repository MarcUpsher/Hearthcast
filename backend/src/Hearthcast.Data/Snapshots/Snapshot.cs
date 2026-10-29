using Hearthcast.Data.Households;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Data.Snapshots;

public sealed class Snapshot
{
    private readonly List<SnapshotPerson> _people = [];
    private readonly List<ItemSnapshot> _itemSnapshots = [];

    private Snapshot()
    {
    }

    internal Snapshot(Household household, DateOnly date, DatePrecision precision,
        IReadOnlyCollection<TrackedItem> items, IReadOnlyCollection<ItemSnapshotValue> values)
    {
        if (!Enum.IsDefined(precision))
        {
            throw new ArgumentOutOfRangeException(nameof(precision));
        }

        if (!household.People.Any(person => person.ParticipatesInHouseholdFinances))
        {
            throw new InvalidOperationException("A snapshot requires at least one household participant.");
        }

        if (values.Select(value => value.TrackedItemId).Distinct().Count() != values.Count)
        {
            throw new ArgumentException("Each tracked item can have only one value in a snapshot.", nameof(values));
        }

        var itemIds = items.Select(item => item.Id).ToHashSet();
        if (values.Any(value => !itemIds.Contains(value.TrackedItemId)))
        {
            throw new ArgumentException("Values must refer to active tracked items in this household.", nameof(values));
        }

        Id = Guid.NewGuid();
        HouseholdId = household.Id;
        HouseholdName = household.Name;
        SnapshotDate = precision == DatePrecision.Month ? new DateOnly(date.Year, date.Month, 1) : date;
        DatePrecision = precision;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        _people.AddRange(household.People.Select(person => new SnapshotPerson(this, person)));
        var people = _people.ToDictionary(person => person.PersonId);
        var amounts = values.ToDictionary(value => value.TrackedItemId, value => value.Amount);
        _itemSnapshots.AddRange(items.Select(item => new ItemSnapshot(this, item, people, amounts.GetValueOrDefault(item.Id))));
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string HouseholdName { get; private set; } = string.Empty;
    public DateOnly SnapshotDate { get; private set; }
    public DatePrecision DatePrecision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<SnapshotPerson> People => _people.AsReadOnly();
    public IReadOnlyCollection<ItemSnapshot> ItemSnapshots => _itemSnapshots.AsReadOnly();
}
