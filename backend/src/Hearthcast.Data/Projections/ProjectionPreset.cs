using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections;

public sealed class ProjectionPreset
{
    private readonly List<ProjectionPresetCurrency> _currencies = [];
    private readonly List<ProjectionPresetGroup> _groups = [];
    private readonly List<ProjectionPresetFinancedAsset> _financedAssets = [];
    private ProjectionPreset() { }
    internal ProjectionPreset(Guid householdId)
    {
        Id = Guid.NewGuid();
        HouseholdId = householdId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string CurrencyCode { get; private set; } = string.Empty;
    public int DurationYears { get; private set; }
    public decimal AnnualInflationPercent { get; private set; }
    public bool ShowInflationAdjustedValues { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ProjectionPresetCurrency> Currencies => _currencies.AsReadOnly();
    public IReadOnlyCollection<ProjectionPresetGroup> Groups => _groups.AsReadOnly();
    public IReadOnlyCollection<ProjectionPresetFinancedAsset> FinancedAssets => _financedAssets.AsReadOnly();

    internal void Update(ProjectionPresetSettings settings, IReadOnlyCollection<ProjectionGroupSelection> groups,
        IReadOnlyCollection<ProjectionFinancedAssetSettings> assets)
    {
        settings = ProjectionRules.Normalize(settings);
        assets = assets.Select(ProjectionRules.NormalizeLoan).ToArray();
        var name = RequiredText.Trim(settings.Name);
        var currency = RequiredAlphabeticCode.Normalize(settings.CurrencyCode, 3);
        ProjectionRules.ValidateAssumptions(settings);
        ProjectionRules.ValidateSelections(groups, assets);
        ProjectionRules.ValidateDestinations(assets, settings.Currencies);
        Name = name;
        CurrencyCode = currency;
        DurationYears = settings.DurationYears;
        AnnualInflationPercent = settings.AnnualInflationPercent;
        ShowInflationAdjustedValues = settings.ShowInflationAdjustedValues;
        _currencies.RemoveAll(row => !settings.Currencies.Any(c => c.CurrencyCode == row.CurrencyCode));
        foreach (var currencySettings in settings.Currencies)
        {
            var existing = _currencies.SingleOrDefault(c => c.CurrencyCode == currencySettings.CurrencyCode);
            if (existing is null) _currencies.Add(new(this, currencySettings));
            else existing.Update(currencySettings);
        }
        // Update retained children in place: EF tracks their composite identities.
        _groups.RemoveAll(row => !groups.Any(g => g.GroupId == row.GroupId));
        foreach (var selection in groups)
        {
            var existing = _groups.SingleOrDefault(g => g.GroupId == selection.GroupId);
            if (existing is null) _groups.Add(new(this, selection));
            else existing.SetBucket(selection.Bucket);
        }
        _financedAssets.RemoveAll(row => !assets.Any(a => a.FinancedAssetId == row.FinancedAssetId));
        foreach (var settingsRow in assets)
        {
            var existing = _financedAssets.SingleOrDefault(a => a.FinancedAssetId == settingsRow.FinancedAssetId);
            if (existing is null) _financedAssets.Add(new(this, settingsRow));
            else existing.Update(settingsRow);
        }
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
