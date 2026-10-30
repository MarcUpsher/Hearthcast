using Hearthcast.Data.Households;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Data.Snapshots;

public sealed class Snapshot
{
    private readonly List<SnapshotPerson> _people = [];
    private readonly List<ItemSnapshot> _itemSnapshots = [];
    private readonly List<FinancedAssetSnapshot> _financedAssetSnapshots = [];

    private Snapshot()
    {
    }

    internal Snapshot(Household household, DateOnly date, DatePrecision precision,
        IReadOnlyCollection<TrackedItem> items, IReadOnlyCollection<ItemSnapshotValue> values,
        IReadOnlyCollection<FinancedAsset> assets, IReadOnlyCollection<FinancedAssetSnapshotValue> financedValues,
        Snapshot? carrySource = null)
    {
        var period = SnapshotPeriod.From(date, precision);
        if (carrySource is not null && (carrySource.HouseholdId != household.Id
            || carrySource.SnapshotDate > period.End))
        {
            throw new ArgumentException("The carry source must be in this household and cannot be definitely later than the capture.", nameof(carrySource));
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

        ValidateFinancedInputs(financedValues);
        var assetIds = assets.Select(asset => asset.Id).ToHashSet();
        if (financedValues.Any(value => !assetIds.Contains(value.FinancedAssetId)))
        {
            throw new ArgumentException("Values must refer to active financed assets in this household.", nameof(financedValues));
        }

        Id = Guid.NewGuid();
        HouseholdId = household.Id;
        HouseholdName = household.Name;
        SnapshotDate = period.Start;
        DatePrecision = precision;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Revision = Guid.NewGuid();
        _people.AddRange(household.People.Select(person => new SnapshotPerson(this, person)));
        var people = _people.ToDictionary(person => person.PersonId);
        var amounts = values.ToDictionary(value => value.TrackedItemId, value => value.Amount);
        var previous = carrySource?.ItemSnapshots.ToDictionary(item => item.TrackedItemId);
        foreach (var item in items)
        {
            var supplied = amounts.TryGetValue(item.Id, out var amount);
            var source = supplied ? null : previous?.GetValueOrDefault(item.Id);
            _itemSnapshots.Add(new ItemSnapshot(this, item, people, amount, source));
        }
        var previousAssets = carrySource?.FinancedAssetSnapshots.ToDictionary(asset => asset.FinancedAssetId);
        foreach (var asset in assets)
        {
            var supplied = financedValues.Where(value => value.FinancedAssetId == asset.Id)
                .ToDictionary(value => value.Balance, value => value.Amount);
            _financedAssetSnapshots.Add(new FinancedAssetSnapshot(this, asset, people, supplied,
                previousAssets?.GetValueOrDefault(asset.Id)));
        }
    }

    internal void CorrectValues(Guid expectedRevision, IReadOnlyCollection<ItemSnapshotValue> values,
        IReadOnlyCollection<FinancedAssetSnapshotValue> financedValues)
    {
        if (Revision != expectedRevision)
        {
            throw new SnapshotConflictException();
        }

        if ((values.Count == 0 && financedValues.Count == 0) || values.Select(value => value.TrackedItemId).Distinct().Count() != values.Count)
        {
            throw new ArgumentException("Supply at least one value, with each tracked item appearing once.", nameof(values));
        }

        var entries = _itemSnapshots.ToDictionary(item => item.TrackedItemId);
        if (values.Any(value => !entries.ContainsKey(value.TrackedItemId)))
        {
            throw new ArgumentException("Corrections must refer to existing items in this saved snapshot.", nameof(values));
        }

        if (values.Any(value => value.Amount < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Recorded amounts cannot be negative.");
        }

        ValidateFinancedInputs(financedValues);
        var assets = _financedAssetSnapshots.ToDictionary(asset => asset.FinancedAssetId);
        foreach (var value in financedValues)
        {
            if (!assets.TryGetValue(value.FinancedAssetId, out var asset))
            {
                throw new ArgumentException("Corrections must refer to financed assets in this saved snapshot.", nameof(financedValues));
            }
            asset.ValidateValue(value.Balance, value.Amount);
        }

        foreach (var value in values)
        {
            entries[value.TrackedItemId].RecordValue(this, value.Amount);
        }

        foreach (var value in financedValues)
        {
            assets[value.FinancedAssetId].RecordValue(this, value.Balance, value.Amount);
        }
        Revision = Guid.NewGuid();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void ValidateFinancedInputs(IReadOnlyCollection<FinancedAssetSnapshotValue> values)
    {
        if (values.Select(value => (value.FinancedAssetId, value.Balance)).Distinct().Count() != values.Count)
        {
            throw new ArgumentException("Supply each financed-asset balance only once.", nameof(values));
        }
        if (values.Any(value => !Enum.IsDefined(value.Balance) || value.Amount < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(values), "Select a valid balance with a nonnegative amount or null.");
        }
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string HouseholdName { get; private set; } = string.Empty;
    public DateOnly SnapshotDate { get; private set; }
    public DatePrecision DatePrecision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Revision { get; private set; }
    public IReadOnlyCollection<SnapshotPerson> People => _people.AsReadOnly();
    public IReadOnlyCollection<ItemSnapshot> ItemSnapshots => _itemSnapshots.AsReadOnly();
    public IReadOnlyCollection<FinancedAssetSnapshot> FinancedAssetSnapshots => _financedAssetSnapshots.AsReadOnly();
}
