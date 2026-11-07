namespace Hearthcast.Data.Projections;

public sealed class ProjectionPresetCurrency
{
    private ProjectionPresetCurrency() { }
    internal ProjectionPresetCurrency(ProjectionPreset preset, ProjectionCurrencySettings settings)
    {
        ProjectionPresetId = preset.Id;
        CurrencyCode = settings.CurrencyCode;
        Update(settings);
    }
    public Guid ProjectionPresetId { get; private set; }
    public string CurrencyCode { get; private set; } = string.Empty;
    public decimal MonthlyInvestmentContribution { get; private set; }
    public decimal AnnualInvestmentReturnPercent { get; private set; }
    public decimal MonthlySavingsContribution { get; private set; }
    public decimal AnnualSavingsInterestPercent { get; private set; }
    internal void Update(ProjectionCurrencySettings s)
    {
        MonthlyInvestmentContribution = s.MonthlyInvestmentContribution;
        AnnualInvestmentReturnPercent = s.AnnualInvestmentReturnPercent;
        MonthlySavingsContribution = s.MonthlySavingsContribution;
        AnnualSavingsInterestPercent = s.AnnualSavingsInterestPercent;
    }
}
