using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Snapshots;

public sealed class FinancedAssetSnapshot
{
    private readonly List<FinancedAssetSnapshotOwner> _owners = [];
    private readonly List<FinancedAssetSnapshotLoanHolder> _loanHolders = [];
    private readonly List<FinancedAssetSnapshotHouseholdAllocation> _householdAllocations = [];

    private FinancedAssetSnapshot()
    {
    }

    internal FinancedAssetSnapshot(Snapshot snapshot, FinancedAsset asset,
        IReadOnlyDictionary<Guid, SnapshotPerson> people,
        IReadOnlyDictionary<FinancedAssetBalanceKind, decimal?> values, FinancedAssetSnapshot? source)
    {
        if (asset.HouseholdId != snapshot.HouseholdId || asset.Group.Category.FinancialType != FinancialType.Asset)
        {
            throw new InvalidOperationException("A financed-asset capture requires Asset classification in this household.");
        }
        Id = Guid.NewGuid();
        HouseholdId = snapshot.HouseholdId;
        SnapshotId = snapshot.Id;
        FinancedAssetId = asset.Id;
        AssetName = asset.Name;
        GroupId = asset.GroupId;
        GroupName = asset.Group.Name;
        CategoryId = asset.Group.CategoryId;
        CategoryName = asset.Group.Category.Name;
        FinancialType = asset.Group.Category.FinancialType;
        CurrencyCode = asset.CurrencyCode;
        CountryCode = asset.CountryCode;
        PurchaseDate = asset.PurchaseDate;
        PurchasePrice = asset.PurchasePrice;
        OriginalLoanAmount = asset.OriginalLoanAmount;
        HasWithdrawalFacility = asset.HasWithdrawalFacility;
        IncludeInHousehold = asset.IncludeInHousehold;
        _owners.AddRange(asset.Owners.Select(row => new FinancedAssetSnapshotOwner(this, people[row.PersonId], row.ShareBasisPoints)));
        _loanHolders.AddRange(asset.LoanHolders.Select(row => new FinancedAssetSnapshotLoanHolder(this, people[row.PersonId])));
        _householdAllocations.AddRange(asset.HouseholdAllocations.Select(row =>
            new FinancedAssetSnapshotHouseholdAllocation(this, people[row.PersonId], row.ShareBasisPoints)));
        if (_owners.Sum(row => (long)row.ShareBasisPoints) != AllocationShareRules.FullShareBasisPoints
            || (IncludeInHousehold ? _householdAllocations.Sum(row => (long)row.ShareBasisPoints) != AllocationShareRules.FullShareBasisPoints
                : _householdAllocations.Count != 0))
        {
            throw new InvalidOperationException("Current ownership and household allocations must be complete and consistent.");
        }
        foreach (var balance in Enum.GetValues<FinancedAssetBalanceKind>())
        {
            if (values.TryGetValue(balance, out var amount))
            {
                RecordValue(snapshot, balance, amount);
            }
            else if (source is not null && (balance != FinancedAssetBalanceKind.AvailableToWithdraw || HasWithdrawalFacility))
            {
                CarryValue(snapshot, balance, source);
            }
            else
            {
                RecordValue(snapshot, balance, null);
            }
        }
    }

    internal void ValidateValue(FinancedAssetBalanceKind balance, decimal? amount)
    {
        if (!Enum.IsDefined(balance)) throw new ArgumentOutOfRangeException(nameof(balance));
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Recorded balances cannot be negative.");
        if (balance == FinancedAssetBalanceKind.AvailableToWithdraw && !HasWithdrawalFacility && amount.HasValue)
        {
            throw new Hearthcast.Data.Validation.InvalidValueException("This asset has no withdrawal facility.", nameof(amount));
        }
    }

    internal void RecordValue(Snapshot snapshot, FinancedAssetBalanceKind balance, decimal? amount)
    {
        ValidateValue(balance, amount);
        SetBalance(balance, new BalanceState(amount, amount.HasValue ? SnapshotValueOrigin.Confirmed : SnapshotValueOrigin.Unrecorded,
            amount.HasValue ? snapshot.SnapshotDate : null, amount.HasValue ? snapshot.DatePrecision : null, null));
    }

    private void CarryValue(Snapshot snapshot, FinancedAssetBalanceKind balance, FinancedAssetSnapshot source)
    {
        if (source.FinancedAssetId != FinancedAssetId || source.CurrencyCode != CurrencyCode)
        {
            throw new Hearthcast.Data.Validation.InvalidValueException("A carry requires the same asset and currency. Supply fresh balances or explicitly clear them.");
        }
        var state = source.GetBalance(balance);
        if (!state.Amount.HasValue)
        {
            RecordValue(snapshot, balance, null);
            return;
        }
        if (state.ConfirmedDate > SnapshotPeriod.From(snapshot.SnapshotDate, snapshot.DatePrecision).End)
        {
            throw new Hearthcast.Data.Validation.BusinessRuleViolationException("A future confirmation cannot be carried into this snapshot.");
        }
        SetBalance(balance, state with { Origin = SnapshotValueOrigin.CarriedForward, SourceId = source.Id });
    }

    private BalanceState GetBalance(FinancedAssetBalanceKind balance) => balance switch
    {
        FinancedAssetBalanceKind.CurrentValuation => new(CurrentValuation, CurrentValuationValueOrigin, CurrentValuationLastConfirmedDate, CurrentValuationLastConfirmedPrecision, CurrentValuationCarriedFromSnapshotId),
        FinancedAssetBalanceKind.OutstandingLoanBalance => new(OutstandingLoanBalance, OutstandingLoanBalanceValueOrigin, OutstandingLoanBalanceLastConfirmedDate, OutstandingLoanBalanceLastConfirmedPrecision, OutstandingLoanBalanceCarriedFromSnapshotId),
        FinancedAssetBalanceKind.AvailableToWithdraw => new(AvailableToWithdraw, AvailableToWithdrawValueOrigin, AvailableToWithdrawLastConfirmedDate, AvailableToWithdrawLastConfirmedPrecision, AvailableToWithdrawCarriedFromSnapshotId),
        _ => throw new ArgumentOutOfRangeException(nameof(balance))
    };

    private void SetBalance(FinancedAssetBalanceKind balance, BalanceState state)
    {
        switch (balance)
        {
            case FinancedAssetBalanceKind.CurrentValuation:
                CurrentValuation = state.Amount;
                CurrentValuationValueOrigin = state.Origin;
                CurrentValuationLastConfirmedDate = state.ConfirmedDate;
                CurrentValuationLastConfirmedPrecision = state.Precision;
                CurrentValuationCarriedFromSnapshotId = state.SourceId;
                break;
            case FinancedAssetBalanceKind.OutstandingLoanBalance:
                OutstandingLoanBalance = state.Amount;
                OutstandingLoanBalanceValueOrigin = state.Origin;
                OutstandingLoanBalanceLastConfirmedDate = state.ConfirmedDate;
                OutstandingLoanBalanceLastConfirmedPrecision = state.Precision;
                OutstandingLoanBalanceCarriedFromSnapshotId = state.SourceId;
                break;
            case FinancedAssetBalanceKind.AvailableToWithdraw:
                AvailableToWithdraw = state.Amount;
                AvailableToWithdrawValueOrigin = state.Origin;
                AvailableToWithdrawLastConfirmedDate = state.ConfirmedDate;
                AvailableToWithdrawLastConfirmedPrecision = state.Precision;
                AvailableToWithdrawCarriedFromSnapshotId = state.SourceId;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(balance));
        }
    }

    private readonly record struct BalanceState(decimal? Amount, SnapshotValueOrigin Origin,
        DateOnly? ConfirmedDate, DatePrecision? Precision, Guid? SourceId);

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid SnapshotId { get; private set; }
    public Guid FinancedAssetId { get; private set; }
    public string AssetName { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public string CategoryName { get; private set; } = string.Empty;
    public FinancialType FinancialType { get; private set; }
    public Guid GroupId { get; private set; }
    public string GroupName { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchasePrice { get; private set; }
    public decimal OriginalLoanAmount { get; private set; }
    public bool HasWithdrawalFacility { get; private set; }
    public bool IncludeInHousehold { get; private set; }
    public IReadOnlyCollection<FinancedAssetSnapshotOwner> Owners => _owners.AsReadOnly();
    public IReadOnlyCollection<FinancedAssetSnapshotLoanHolder> LoanHolders => _loanHolders.AsReadOnly();
    public IReadOnlyCollection<FinancedAssetSnapshotHouseholdAllocation> HouseholdAllocations => _householdAllocations.AsReadOnly();

    public decimal? CurrentValuation { get; private set; }
    public SnapshotValueOrigin CurrentValuationValueOrigin { get; private set; }
    public DateOnly? CurrentValuationLastConfirmedDate { get; private set; }
    public DatePrecision? CurrentValuationLastConfirmedPrecision { get; private set; }
    public Guid? CurrentValuationCarriedFromSnapshotId { get; private set; }

    public decimal? OutstandingLoanBalance { get; private set; }
    public SnapshotValueOrigin OutstandingLoanBalanceValueOrigin { get; private set; }
    public DateOnly? OutstandingLoanBalanceLastConfirmedDate { get; private set; }
    public DatePrecision? OutstandingLoanBalanceLastConfirmedPrecision { get; private set; }
    public Guid? OutstandingLoanBalanceCarriedFromSnapshotId { get; private set; }

    public decimal? AvailableToWithdraw { get; private set; }
    public SnapshotValueOrigin AvailableToWithdrawValueOrigin { get; private set; }
    public DateOnly? AvailableToWithdrawLastConfirmedDate { get; private set; }
    public DatePrecision? AvailableToWithdrawLastConfirmedPrecision { get; private set; }
    public Guid? AvailableToWithdrawCarriedFromSnapshotId { get; private set; }

    public decimal? CurrentEquity => CurrentValuation - OutstandingLoanBalance;
    public decimal? LoanBalanceReduction => OriginalLoanAmount - OutstandingLoanBalance;
    /// <summary>Returns null when there is no withdrawal facility or a required balance is missing.</summary>
    /// <exception cref="OverflowException">The calculated reduction is outside the decimal range.</exception>
    public decimal? LoanBalanceReductionExcludingWithdrawableFunds
    {
        get
        {
            try
            {
                return HasWithdrawalFacility
                    ? OriginalLoanAmount - OutstandingLoanBalance - AvailableToWithdraw : null;
            }
            catch (OverflowException exception)
            {
                throw new OverflowException(
                    "The loan balance reduction excluding withdrawable funds is outside the decimal range.", exception);
            }
        }
    }
}
