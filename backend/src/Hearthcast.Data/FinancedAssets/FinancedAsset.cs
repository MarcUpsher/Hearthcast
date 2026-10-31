using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.FinancedAssets;

public sealed class FinancedAsset
{
    private readonly List<FinancedAssetOwner> _owners = [];
    private readonly List<FinancedAssetLoanHolder> _loanHolders = [];
    private readonly List<FinancedAssetHouseholdAllocation> _householdAllocations = [];

    private FinancedAsset()
    {
    }

    internal FinancedAsset(ItemGroup group, string name, string currencyCode, string countryCode,
        DateOnly? purchaseDate, decimal? purchasePrice, decimal originalLoanAmount, bool hasWithdrawalFacility)
    {
        if (group.Category.FinancialType != FinancialType.Asset)
        {
            throw new InvalidValueException("A financed asset requires a group in an Asset category.", nameof(group));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(originalLoanAmount);
        if (purchasePrice < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(purchasePrice));
        }
        Id = Guid.NewGuid();
        HouseholdId = group.HouseholdId;
        Group = group;
        GroupId = group.Id;
        Rename(name);
        CurrencyCode = RequiredAlphabeticCode.Normalize(currencyCode, 3);
        CountryCode = RequiredAlphabeticCode.Normalize(countryCode, 2);
        PurchaseDate = purchaseDate;
        PurchasePrice = purchasePrice;
        OriginalLoanAmount = originalLoanAmount;
        HasWithdrawalFacility = hasWithdrawalFacility;
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid GroupId { get; private set; }
    public ItemGroup Group { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchasePrice { get; private set; }
    public decimal OriginalLoanAmount { get; private set; }
    public bool HasWithdrawalFacility { get; private set; }
    public bool IncludeInHousehold { get; private set; }
    public bool IsRetired { get; private set; }
    public IReadOnlyCollection<FinancedAssetOwner> Owners => _owners.AsReadOnly();
    public IReadOnlyCollection<FinancedAssetLoanHolder> LoanHolders => _loanHolders.AsReadOnly();
    public IReadOnlyCollection<FinancedAssetHouseholdAllocation> HouseholdAllocations => _householdAllocations.AsReadOnly();

    public void Rename(string name) => Name = RequiredText.Trim(name);
    public void SetRetired(bool isRetired) => IsRetired = isRetired;

    internal void SetPersonAssociations(
        IReadOnlyCollection<(Person Person, int ShareBasisPoints)> owners,
        IReadOnlyCollection<Person> loanHolders, bool includeInHousehold,
        IReadOnlyCollection<(Person Person, int ShareBasisPoints)> allocations)
    {
        ValidateShares(owners, requireFullShare: true);
        ValidatePeople(loanHolders);
        ValidateShares(allocations, requireFullShare: includeInHousehold);
        if (!includeInHousehold && allocations.Count != 0)
        {
            throw new ArgumentException("Excluded assets cannot have household allocations.", nameof(allocations));
        }
        if (allocations.Any(share => !share.Person.ParticipatesInHouseholdFinances))
        {
            throw new BusinessRuleViolationException("Only household participants can receive an allocation.");
        }

        // Validate the complete change before modifying any association.
        _owners.RemoveAll(row => !owners.Any(share => share.Person.Id == row.PersonId));
        foreach (var share in owners)
        {
            var existing = _owners.SingleOrDefault(row => row.PersonId == share.Person.Id);
            if (existing is null)
            {
                _owners.Add(new FinancedAssetOwner(this, share.Person, share.ShareBasisPoints));
            }
            else
            {
                existing.SetShare(share.ShareBasisPoints);
            }
        }
        _loanHolders.RemoveAll(row => !loanHolders.Any(person => person.Id == row.PersonId));
        foreach (var person in loanHolders)
        {
            if (!_loanHolders.Any(row => row.PersonId == person.Id))
            {
                _loanHolders.Add(new FinancedAssetLoanHolder(this, person));
            }
        }
        _householdAllocations.RemoveAll(row => !allocations.Any(share => share.Person.Id == row.PersonId));
        foreach (var share in allocations)
        {
            var existing = _householdAllocations.SingleOrDefault(row => row.PersonId == share.Person.Id);
            if (existing is null)
            {
                _householdAllocations.Add(new FinancedAssetHouseholdAllocation(this, share.Person, share.ShareBasisPoints));
            }
            else
            {
                existing.SetShare(share.ShareBasisPoints);
            }
        }
        IncludeInHousehold = includeInHousehold;
    }

    private void ValidatePeople(IEnumerable<Person> people)
    {
        var requested = people.ToArray();
        if (requested.Any(person => person.HouseholdId != HouseholdId)
            || requested.Select(person => person.Id).Distinct().Count() != requested.Length)
        {
            throw new ArgumentException("Each person must belong to this household and appear only once per association.");
        }
    }

    private void ValidateShares(IReadOnlyCollection<(Person Person, int ShareBasisPoints)> shares, bool requireFullShare)
    {
        ValidatePeople(shares.Select(share => share.Person));
        foreach (var share in shares)
        {
            AllocationShareRules.EnsureValid(share.ShareBasisPoints);
        }
        if (requireFullShare && shares.Sum(share => (long)share.ShareBasisPoints) != AllocationShareRules.FullShareBasisPoints)
        {
            throw new ArgumentException("Ownership and included household allocations must each total 100%.");
        }
    }
}
