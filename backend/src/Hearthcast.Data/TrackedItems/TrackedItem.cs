using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.TrackedItems;

public sealed class TrackedItem
{
    private readonly List<TrackedItemHouseholdAllocation> _householdAllocations = [];

    private TrackedItem()
    {
    }

    internal TrackedItem(ItemGroup group, Person holder, string name, string currencyCode, string countryCode)
    {
        if (holder.HouseholdId != group.HouseholdId)
        {
            throw new ArgumentException("The holder and group must belong to the same household.");
        }

        Id = Guid.NewGuid();
        HouseholdId = group.HouseholdId;
        Group = group;
        GroupId = group.Id;
        HolderPerson = holder;
        HolderPersonId = holder.Id;
        Rename(name);
        CurrencyCode = RequiredAlphabeticCode.Normalize(currencyCode, 3);
        CountryCode = RequiredAlphabeticCode.Normalize(countryCode, 2);
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid GroupId { get; private set; }
    public ItemGroup Group { get; private set; } = null!;
    public Guid HolderPersonId { get; private set; }
    public Person HolderPerson { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public bool IncludeInHousehold { get; private set; }
    public bool IsRetired { get; private set; }
    public IReadOnlyCollection<TrackedItemHouseholdAllocation> HouseholdAllocations =>
        _householdAllocations.AsReadOnly();

    public void Rename(string name)
    {
        Name = RequiredText.Trim(name);
    }

    public void SetRetired(bool isRetired)
    {
        IsRetired = isRetired;
    }

    internal IReadOnlyCollection<TrackedItemHouseholdAllocation> SetHouseholdAllocation(
        bool includeInHousehold,
        IReadOnlyCollection<(Person Person, int ShareBasisPoints)> shares)
    {
        if (!includeInHousehold && shares.Count != 0)
        {
            throw new ArgumentException("Excluded items cannot have household allocations.", nameof(shares));
        }

        var personIds = shares.Select(share => share.Person.Id).ToHashSet();
        if (personIds.Count != shares.Count)
        {
            throw new ArgumentException("Each person can appear only once in an allocation.", nameof(shares));
        }

        foreach (var share in shares)
        {
            TrackedItemHouseholdAllocation.Validate(share.Person, HouseholdId, share.ShareBasisPoints);
        }

        if (includeInHousehold && shares.Sum(share => (long)share.ShareBasisPoints) != AllocationShareRules.FullShareBasisPoints)
        {
            throw new ArgumentException("Household allocations must total 100%.", nameof(shares));
        }

        var removed = _householdAllocations.Where(allocation => !personIds.Contains(allocation.PersonId)).ToArray();
        _householdAllocations.RemoveAll(allocation => !personIds.Contains(allocation.PersonId));
        foreach (var share in shares)
        {
            var allocation = _householdAllocations.SingleOrDefault(allocation => allocation.PersonId == share.Person.Id);
            if (allocation is null)
            {
                _householdAllocations.Add(new TrackedItemHouseholdAllocation(this, share.Person, share.ShareBasisPoints));
            }
            else
            {
                allocation.SetShare(share.ShareBasisPoints);
            }
        }

        IncludeInHousehold = includeInHousehold;
        return removed;
    }

}
