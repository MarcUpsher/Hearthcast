using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections;

public sealed class ProjectionPreset
{
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
    public decimal MonthlyInvestmentContribution { get; private set; }
    public decimal AnnualInvestmentReturnPercent { get; private set; }
    public decimal MonthlySavingsContribution { get; private set; }
    public decimal AnnualSavingsInterestPercent { get; private set; }
    public decimal AnnualInflationPercent { get; private set; }
    public bool ShowInflationAdjustedValues { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<ProjectionPresetGroup> Groups => _groups.AsReadOnly();
    public IReadOnlyCollection<ProjectionPresetFinancedAsset> FinancedAssets => _financedAssets.AsReadOnly();

    internal void Update(ProjectionPresetSettings settings, IReadOnlyCollection<ProjectionGroupSelection> groups,
        IReadOnlyCollection<ProjectionFinancedAssetSettings> assets)
    {
        var name = RequiredText.Trim(settings.Name);
        var currency = RequiredAlphabeticCode.Normalize(settings.CurrencyCode, 3);
        ProjectionRules.ValidateAssumptions(settings);
        ProjectionRules.ValidateSelections(groups, assets);
        Name = name;
        CurrencyCode = currency;
        DurationYears = settings.DurationYears;
        MonthlyInvestmentContribution = settings.MonthlyInvestmentContribution;
        AnnualInvestmentReturnPercent = settings.AnnualInvestmentReturnPercent;
        MonthlySavingsContribution = settings.MonthlySavingsContribution;
        AnnualSavingsInterestPercent = settings.AnnualSavingsInterestPercent;
        AnnualInflationPercent = settings.AnnualInflationPercent;
        ShowInflationAdjustedValues = settings.ShowInflationAdjustedValues;
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
