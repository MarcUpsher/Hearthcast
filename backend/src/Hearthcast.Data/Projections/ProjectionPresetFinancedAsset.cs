namespace Hearthcast.Data.Projections;

public sealed class ProjectionPresetFinancedAsset
{
    private ProjectionPresetFinancedAsset() { }
    internal ProjectionPresetFinancedAsset(ProjectionPreset preset, ProjectionFinancedAssetSettings settings)
    {
        ProjectionPresetId = preset.Id;
        HouseholdId = preset.HouseholdId;
        FinancedAssetId = settings.FinancedAssetId;
        Update(settings);
    }
    public Guid ProjectionPresetId { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid FinancedAssetId { get; private set; }
    /// <summary>Payment in the referenced asset's native currency, independent of the preset currency.</summary>
    public decimal MonthlyLoanPaymentInAssetCurrency { get; private set; }
    public decimal ExtraMonthlyLoanPaymentInAssetCurrency { get; private set; }
    public string InvestmentCurrencyAfterPayoff { get; private set; } = string.Empty;
    public decimal AnnualLoanInterestPercent { get; private set; }
    public decimal AnnualAssetAppreciationPercent { get; private set; }

    internal void Update(ProjectionFinancedAssetSettings settings)
    {
        MonthlyLoanPaymentInAssetCurrency = settings.MonthlyLoanPaymentInAssetCurrency;
        ExtraMonthlyLoanPaymentInAssetCurrency = settings.ExtraMonthlyLoanPaymentInAssetCurrency;
        InvestmentCurrencyAfterPayoff = settings.InvestmentCurrencyAfterPayoff;
        AnnualLoanInterestPercent = settings.AnnualLoanInterestPercent;
        AnnualAssetAppreciationPercent = settings.AnnualAssetAppreciationPercent;
    }
}
