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
        IReadOnlyDictionary<Guid, SnapshotPerson> people, decimal? amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Recorded amounts cannot be negative.");
        }

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
        Amount = amount;
        ValueOrigin = amount.HasValue ? SnapshotValueOrigin.Confirmed : SnapshotValueOrigin.Unrecorded;
        LastConfirmedDate = amount.HasValue ? snapshot.SnapshotDate : null;
        LastConfirmedPrecision = amount.HasValue ? snapshot.DatePrecision : null;

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
    public IReadOnlyCollection<ItemSnapshotHouseholdAllocation> HouseholdAllocations =>
        _householdAllocations.AsReadOnly();
}
