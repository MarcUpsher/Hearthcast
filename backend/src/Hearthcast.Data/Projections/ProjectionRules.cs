using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections;

internal static class ProjectionRules
{
    public static void ValidateAssumptions(ProjectionPresetSettings settings)
    {
        if (settings.DurationYears is < 1 or > 100)
            throw new InvalidValueException("Duration must be between 1 and 100 years.", nameof(settings.DurationYears));
        Contribution(settings.MonthlyInvestmentContribution, nameof(settings.MonthlyInvestmentContribution));
        Contribution(settings.MonthlySavingsContribution, nameof(settings.MonthlySavingsContribution));
        Rate(settings.AnnualInvestmentReturnPercent, nameof(settings.AnnualInvestmentReturnPercent));
        Rate(settings.AnnualSavingsInterestPercent, nameof(settings.AnnualSavingsInterestPercent));
        Rate(settings.AnnualInflationPercent, nameof(settings.AnnualInflationPercent));
    }

    public static void ValidateLoan(ProjectionFinancedAssetSettings asset)
    {
        Contribution(asset.MonthlyLoanPaymentInAssetCurrency, nameof(asset.MonthlyLoanPaymentInAssetCurrency));
        Rate(asset.AnnualLoanInterestPercent, nameof(asset.AnnualLoanInterestPercent), false);
        Rate(asset.AnnualAssetAppreciationPercent, nameof(asset.AnnualAssetAppreciationPercent));
    }

    public static void ValidateSelections(IReadOnlyCollection<ProjectionGroupSelection> groups,
        IReadOnlyCollection<ProjectionFinancedAssetSettings> assets)
    {
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
            ProjectionRules.ValidateLoan(asset);
        }
    }

    public static void Contribution(decimal value, string name)
    {
        if (value < 0) throw new InvalidValueException("Amounts cannot be negative.", name);
    }

    public static void Rate(decimal value, string name, bool allowNegative = true)
    {
        if (value > 100 || (allowNegative ? value <= -100 : value < 0))
            throw new InvalidValueException(allowNegative
                ? "Annual percentages must be greater than -100 and at most 100."
                : "Loan interest must be between 0 and 100 percent.", name);
    }
}
