using Hearthcast.Data.Classification;
using Hearthcast.Data.TrackedItems;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Snapshots;

public sealed class ItemSnapshot
{
    private readonly List<ItemSnapshotHouseholdAllocation> _householdAllocations = [];

    private ItemSnapshot()
    {
    }

    internal ItemSnapshot(Snapshot snapshot, TrackedItem item,
        IReadOnlyDictionary<Guid, SnapshotPerson> people, decimal? amount, ItemSnapshot? carrySource = null)
    {
        Id = Guid.NewGuid();
        HouseholdId = snapshot.HouseholdId;
        SnapshotId = snapshot.Id;
        TrackedItemId = item.Id;
        HolderSnapshotPerson = people[item.HolderPersonId];
        HolderSnapshotPersonId = HolderSnapshotPerson.Id;
        ItemName = item.Name;
        GroupId = item.GroupId;
        GroupName = item.Group.Name;
        CategoryId = item.Group.CategoryId;
        CategoryName = item.Group.Category.Name;
        FinancialType = item.Group.Category.FinancialType;
        CurrencyCode = item.CurrencyCode;
        CountryCode = item.CountryCode;
        IncludeInHousehold = item.IncludeInHousehold;
        if (carrySource is null)
        {
            RecordValue(snapshot, amount);
        }
        else
        {
            CarryValue(snapshot, carrySource);
        }

        foreach (var allocation in item.HouseholdAllocations)
        {
            _householdAllocations.Add(new ItemSnapshotHouseholdAllocation(
                this, people[allocation.PersonId], allocation.ShareBasisPoints));
        }

        if (IncludeInHousehold
            ? _householdAllocations.Sum(allocation => (long)allocation.ShareBasisPoints) != AllocationShareRules.FullShareBasisPoints
            : _householdAllocations.Count != 0)
        {
            throw new InvalidOperationException("Current household allocations must match the item's inclusion setting and total 100% when included.");
        }
    }

    internal void RecordValue(Snapshot snapshot, decimal? amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Recorded amounts cannot be negative.");
        }

        Amount = amount;
        ValueOrigin = amount.HasValue ? SnapshotValueOrigin.Confirmed : SnapshotValueOrigin.Unrecorded;
        LastConfirmedDate = amount.HasValue ? snapshot.SnapshotDate : null;
        LastConfirmedPrecision = amount.HasValue ? snapshot.DatePrecision : null;
        CarriedFromItemSnapshotId = null;
    }

    private void CarryValue(Snapshot snapshot, ItemSnapshot source)
    {
        if (source.TrackedItemId != TrackedItemId || source.CurrencyCode != CurrencyCode)
        {
            throw new Hearthcast.Data.Validation.InvalidValueException("A carry requires the same item and currency. Supply a fresh value or explicitly clear it.");
        }

        if (!source.Amount.HasValue)
        {
            RecordValue(snapshot, null);
            return;
        }

        if (source.LastConfirmedDate.HasValue
            && source.LastConfirmedDate.Value > SnapshotPeriod.From(snapshot.SnapshotDate, snapshot.DatePrecision).End)
        {
            throw new Hearthcast.Data.Validation.BusinessRuleViolationException("A future confirmation cannot be carried into this snapshot.");
        }

        Amount = source.Amount;
        ValueOrigin = SnapshotValueOrigin.CarriedForward;
        LastConfirmedDate = source.LastConfirmedDate;
        LastConfirmedPrecision = source.LastConfirmedPrecision;
        CarriedFromItemSnapshotId = source.Id;
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid TrackedItemId { get; private set; }
    public Guid HolderSnapshotPersonId { get; private set; }
    public SnapshotPerson HolderSnapshotPerson { get; private set; } = null!;
    public string ItemName { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public string CategoryName { get; private set; } = string.Empty;
    public FinancialType FinancialType { get; private set; }
    public Guid GroupId { get; private set; }
    public string GroupName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public bool IncludeInHousehold { get; private set; }
    public decimal? Amount { get; private set; }
    public SnapshotValueOrigin ValueOrigin { get; private set; }
    public DateOnly? LastConfirmedDate { get; private set; }
    public DatePrecision? LastConfirmedPrecision { get; private set; }
    public Guid? CarriedFromItemSnapshotId { get; private set; }
    public IReadOnlyCollection<ItemSnapshotHouseholdAllocation> HouseholdAllocations =>
        _householdAllocations.AsReadOnly();
}
