using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections;

internal static class ProjectionRules
{
    public static ProjectionPresetSettings Normalize(ProjectionPresetSettings settings)
    {
        ValidateAssumptions(settings);
        return settings with { CurrencyCode = RequiredAlphabeticCode.Normalize(settings.CurrencyCode, 3),
            Currencies = settings.Currencies.Select(c => c with { CurrencyCode = RequiredAlphabeticCode.Normalize(c.CurrencyCode, 3) }).ToArray(),
            ExpenseTarget = settings.ExpenseTarget is { } target ? target with { CurrencyCode = RequiredAlphabeticCode.Normalize(target.CurrencyCode, 3) } : null };
    }

    public static ProjectionFinancedAssetSettings NormalizeLoan(ProjectionFinancedAssetSettings asset)
    {
        if (asset is null) throw new InvalidValueException("Financed asset selections cannot contain null entries.");
        return asset with { InvestmentCurrencyAfterPayoff = RequiredAlphabeticCode.Normalize(asset.InvestmentCurrencyAfterPayoff, 3) };
    }

    public static void ValidateDestinations(IEnumerable<ProjectionFinancedAssetSettings> assets, IReadOnlyList<ProjectionCurrencySettings> currencies)
    {
        foreach (var asset in assets)
            if (!currencies.Any(c => c.CurrencyCode == asset.InvestmentCurrencyAfterPayoff))
                throw new InvalidValueException("Each loan's investment destination must have a configured currency pot.");
    }

    public static void ValidateAssumptions(ProjectionPresetSettings settings)
    {
        if (settings.DurationYears is < 1 or > 100)
            throw new InvalidValueException("Duration must be between 1 and 100 years.", nameof(settings.DurationYears));
        ArgumentNullException.ThrowIfNull(settings.Currencies);
        if (settings.Currencies.Count == 0 || settings.Currencies.Any(c => c is null))
            throw new InvalidValueException("Supply at least one currency pot, without null entries.");
        var codes = settings.Currencies.Select(c => RequiredAlphabeticCode.Normalize(c.CurrencyCode, 3)).ToArray();
        if (codes.Distinct().Count() != codes.Length) throw new InvalidValueException("Supply each currency pot once.");
        foreach (var c in settings.Currencies)
        {
            Contribution(c.MonthlyInvestmentContribution, nameof(c.MonthlyInvestmentContribution));
            Contribution(c.MonthlySavingsContribution, nameof(c.MonthlySavingsContribution));
            Rate(c.AnnualInvestmentReturnPercent, nameof(c.AnnualInvestmentReturnPercent));
            Rate(c.AnnualSavingsInterestPercent, nameof(c.AnnualSavingsInterestPercent));
        }
        Rate(settings.AnnualInflationPercent, nameof(settings.AnnualInflationPercent));
        if (settings.ExpenseTarget is { } target)
        {
            _ = RequiredAlphabeticCode.Normalize(target.CurrencyCode, 3);
            if (target.AnnualExpenses <= 0)
                throw new InvalidValueException("Annual expenses must be greater than zero.", nameof(target.AnnualExpenses));
            if (target.AnnualWithdrawalRatePercent <= 0 || target.AnnualWithdrawalRatePercent > 100)
                throw new InvalidValueException("The withdrawal rate must be greater than zero and at most 100 percent.", nameof(target.AnnualWithdrawalRatePercent));
        }
    }

    public static void ValidateLoan(ProjectionFinancedAssetSettings asset)
    {
        Contribution(asset.MonthlyLoanPaymentInAssetCurrency, nameof(asset.MonthlyLoanPaymentInAssetCurrency));
        Contribution(asset.ExtraMonthlyLoanPaymentInAssetCurrency, nameof(asset.ExtraMonthlyLoanPaymentInAssetCurrency));
        _ = RequiredAlphabeticCode.Normalize(asset.InvestmentCurrencyAfterPayoff, 3);
        try { _ = asset.MonthlyLoanPaymentInAssetCurrency + asset.ExtraMonthlyLoanPaymentInAssetCurrency; }
        catch (OverflowException) { throw new InvalidValueException("The combined loan payment exceeds supported numeric limits."); }
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
