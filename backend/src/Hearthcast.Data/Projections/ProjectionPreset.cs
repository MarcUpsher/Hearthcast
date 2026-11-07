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
        if (settings.DurationYears is < 1 or > 100)
            throw new InvalidValueException("Duration must be between 1 and 100 years.", nameof(settings.DurationYears));
        ProjectionRules.Contribution(settings.MonthlyInvestmentContribution, nameof(settings.MonthlyInvestmentContribution));
        ProjectionRules.Contribution(settings.MonthlySavingsContribution, nameof(settings.MonthlySavingsContribution));
        ProjectionRules.Rate(settings.AnnualInvestmentReturnPercent, nameof(settings.AnnualInvestmentReturnPercent));
        ProjectionRules.Rate(settings.AnnualSavingsInterestPercent, nameof(settings.AnnualSavingsInterestPercent));
        ProjectionRules.Rate(settings.AnnualInflationPercent, nameof(settings.AnnualInflationPercent));
        if (groups.Any(g => g is null))
            throw new InvalidValueException("Group selections cannot contain null entries.", nameof(groups));
        if (assets.Any(a => a is null))
            throw new InvalidValueException("Financed asset selections cannot contain null entries.", nameof(assets));
        if (groups.Select(g => g.GroupId).Distinct().Count() != groups.Count)
            throw new InvalidValueException("A group can only appear once, in one bucket.");
        if (groups.Any(g => !Enum.IsDefined(g.Bucket))) throw new InvalidValueException("Select Investment or Savings.");
        if (assets.Select(a => a.FinancedAssetId).Distinct().Count() != assets.Count)
            throw new InvalidValueException("A financed asset can only appear once.");
        foreach (var asset in assets)
        {
            ProjectionRules.Contribution(asset.MonthlyLoanPaymentInAssetCurrency, nameof(asset.MonthlyLoanPaymentInAssetCurrency));
            ProjectionRules.Rate(asset.AnnualLoanInterestPercent, nameof(asset.AnnualLoanInterestPercent), false);
            ProjectionRules.Rate(asset.AnnualAssetAppreciationPercent, nameof(asset.AnnualAssetAppreciationPercent));
        }
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
