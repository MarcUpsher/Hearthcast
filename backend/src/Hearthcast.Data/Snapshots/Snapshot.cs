using Hearthcast.Data.Households;
using Hearthcast.Data.Validation;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Data.Snapshots;

public sealed class Snapshot
{
    private readonly List<SnapshotExchangeRate> _exchangeRates = [];
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
            throw new InvalidValueException("Values must refer to active tracked items in this household.", nameof(values));
        }

        ValidateFinancedInputs(financedValues);
        var assetIds = assets.Select(asset => asset.Id).ToHashSet();
        if (financedValues.Any(value => !assetIds.Contains(value.FinancedAssetId)))
        {
            throw new InvalidValueException("Values must refer to active financed assets in this household.", nameof(financedValues));
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

    internal void ValidateExchangeRates(IReadOnlyList<Hearthcast.Data.ExchangeRates.ExchangeRateQuote> quotes)
    {
        if (ExchangeRateLookupDate is null || ExchangeRateBaseCurrency is null)
            throw new InvalidOperationException("This snapshot has no exchange-rate capture context.");
        var missing = MissingExchangeRateCurrencies;
        if (quotes.Count != missing.Count || quotes.Select(q => q.QuoteCurrency).Distinct().Count() != quotes.Count
            || quotes.Any(q => q.BaseCurrency != ExchangeRateBaseCurrency || !missing.Contains(q.QuoteCurrency)
                || q.Rate <= 0 || q.Rate > 999999999999999999m || decimal.Round(q.Rate, 18) != q.Rate
                || q.EffectiveDate == default || q.EffectiveDate > ExchangeRateLookupDate))
            throw new InvalidOperationException("The provider returned incomplete or invalid exchange rates.");

    }

    internal void InitializeExchangeRateContext(DateOnly lookupDate)
    {
        ExchangeRateLookupDate = lookupDate;
        ExchangeRateBaseCurrency = Currencies.FirstOrDefault();
    }

    public IReadOnlyList<string> Currencies => _itemSnapshots.Select(i => i.CurrencyCode)
        .Concat(_financedAssetSnapshots.Select(a => a.CurrencyCode)).Distinct().Order(StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> MissingExchangeRateCurrencies => Currencies
        .Where(currency => currency != ExchangeRateBaseCurrency && !_exchangeRates.Any(rate => rate.QuoteCurrency == currency)).ToArray();
    public string ExchangeRateStatus => ExchangeRateLookupDate is null ? "NotCaptured"
        : Currencies.Count < 2 && MissingExchangeRateCurrencies.Count == 0 ? "NotRequired"
        : MissingExchangeRateCurrencies.Count == 0 ? "Ready" : "Pending";
    public DateOnly? ExchangeRateLookupDate { get; private set; }
    public string? ExchangeRateBaseCurrency { get; private set; }
    public IReadOnlyCollection<SnapshotExchangeRate> ExchangeRates => _exchangeRates.AsReadOnly();

    internal void AddItems(IReadOnlyCollection<TrackedItem> items, IReadOnlyCollection<ItemSnapshotValue> values,
        IReadOnlyCollection<Person> currentPeople)
    {
        if (items.Any(item => item.HouseholdId != HouseholdId || item.IsRetired
            || _itemSnapshots.Any(saved => saved.TrackedItemId == item.Id)))
            throw new InvalidValueException("Added items must be active, in this household and absent from the snapshot.");
        var amounts = values.ToDictionary(value => value.TrackedItemId, value => value.Amount);
        var people = _people.ToDictionary(person => person.PersonId);
        var requiredPeople = items.Select(item => item.HolderPersonId)
            .Concat(items.SelectMany(item => item.HouseholdAllocations.Select(share => share.PersonId))).ToHashSet();
        var newPeople = currentPeople.Where(person => requiredPeople.Contains(person.Id) && !people.ContainsKey(person.Id))
            .Select(person => new SnapshotPerson(this, person)).ToArray();
        foreach (var person in newPeople) people.Add(person.PersonId, person);
        if (requiredPeople.Any(id => !people.ContainsKey(id)))
            throw new InvalidValueException("The item's holder and allocation recipients must belong to this household.");
        if (items.SelectMany(item => item.HouseholdAllocations).Any(share => !people[share.PersonId].ParticipatesInHouseholdFinances))
            throw new BusinessRuleViolationException("The item's household allocation conflicts with participation recorded in this snapshot.");
        var entries = items.Select(item => new ItemSnapshot(this, item, people, amounts[item.Id])).ToArray();
        _people.AddRange(newPeople);
        _itemSnapshots.AddRange(entries);
        if (ExchangeRateLookupDate.HasValue && ExchangeRateBaseCurrency is null)
            ExchangeRateBaseCurrency = Currencies.FirstOrDefault();
        Revision = Guid.NewGuid();
        UpdatedAtUtc = DateTimeOffset.UtcNow;
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
            throw new InvalidValueException("Corrections must refer to existing items in this saved snapshot.", nameof(values));
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
                throw new InvalidValueException("Corrections must refer to financed assets in this saved snapshot.", nameof(financedValues));
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
