using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Pure monthly modelling; never reads or writes persistence or fetches rates.</summary>
public static class ProjectionCalculator
{
    public static ProjectionResult Calculate(ProjectionPresetSettings assumptions, ProjectionStartingPosition starting)
    {
        ArgumentNullException.ThrowIfNull(assumptions);
        ArgumentNullException.ThrowIfNull(starting);
        ArgumentNullException.ThrowIfNull(starting.FinancedAssets);
        var currency = RequiredAlphabeticCode.Normalize(assumptions.CurrencyCode, 3);
        Validate(assumptions, starting, currency);
        try
        {
            return Run(assumptions, starting, currency);
        }
        catch (OverflowException)
        {
            throw new InvalidValueException("The projection exceeds supported numeric limits. Reduce the amounts, rates or duration.");
        }
    }

    private static void Validate(ProjectionPresetSettings a, ProjectionStartingPosition starting, string currency)
    {
        ProjectionRules.ValidateAssumptions(a);
        ProjectionRules.Contribution(starting.Investments, nameof(starting.Investments));
        ProjectionRules.Contribution(starting.Savings, nameof(starting.Savings));
        if (starting.Date.Year + a.DurationYears > 9999)
            throw new InvalidValueException("The projection extends beyond the supported date range.");
        var ids = new HashSet<Guid>();
        foreach (var asset in starting.FinancedAssets)
        {
            if (asset is null || asset.Assumptions is null)
                throw new InvalidValueException("Financed asset inputs and assumptions cannot be null.");
            if (asset.Assumptions.FinancedAssetId == Guid.Empty || !ids.Add(asset.Assumptions.FinancedAssetId))
                throw new InvalidValueException("Each financed asset requires a distinct nonempty identifier.");
            var nativeCurrency = RequiredAlphabeticCode.Normalize(asset.CurrencyCode, 3);
            if (asset.ConversionRate <= 0 || (nativeCurrency == currency && asset.ConversionRate != 1))
                throw new InvalidValueException("Supply a positive conversion rate, or exactly 1 for the projection currency.");
            ProjectionRules.Contribution(asset.AssetValue, nameof(asset.AssetValue));
            ProjectionRules.Contribution(asset.LoanBalance, nameof(asset.LoanBalance));
            ProjectionRules.ValidateLoan(asset.Assumptions);
        }
    }

    private static ProjectionResult Run(ProjectionPresetSettings a, ProjectionStartingPosition starting, string currency)
    {
        var investmentFactor = MonthlyFactor(a.AnnualInvestmentReturnPercent);
        var savingsFactor = MonthlyFactor(a.AnnualSavingsInterestPercent);
        var inflationFactor = MonthlyFactor(a.AnnualInflationPercent);
        var assets = starting.FinancedAssets.Select(asset => new AssetState(asset, starting.Date)).ToArray();
        var months = new List<ProjectionMonth>();
        var warnings = new List<ProjectionWarning>();
        var investments = starting.Investments;
        var savings = starting.Savings;
        var inflation = 1m;
        for (var month = 0; month <= a.DurationYears * 12; month++)
        {
            // Anchor each date to the original day (Jan 31 -> Feb 28 -> Mar 31).
            var date = starting.Date.AddMonths(month);
            var redirected = 0m;
            var loans = new List<ProjectionLoanMonth>();
            foreach (var asset in assets)
            {
                var interest = 0m;
                var payment = 0m;
                var surplus = 0m;
                if (month > 0)
                {
                    asset.Value = Multiply(asset.Value, asset.GrowthFactor);
                    interest = asset.Balance * asset.Input.Assumptions.AnnualLoanInterestPercent / 1200m;
                    var budget = asset.Input.Assumptions.MonthlyLoanPaymentInAssetCurrency;
                    if (asset.Balance > 0 && budget <= interest && !asset.Warned)
                    {
                        warnings.Add(new(asset.Input.Assumptions.FinancedAssetId,
                            ProjectionWarningKind.PaymentDoesNotReduceLoanBalance, date));
                        asset.Warned = true;
                    }
                    var due = asset.Balance + interest;
                    payment = Math.Min(budget, due);
                    asset.Balance = due - payment;
                    surplus = budget - payment;
                    if (asset.Balance == 0) asset.PayoffDate ??= date;
                    redirected += Multiply(surplus, asset.Input.ConversionRate);
                }
                loans.Add(new(asset.Input.Assumptions.FinancedAssetId, asset.Currency, asset.Value,
                    asset.Balance, interest, payment, surplus, asset.PayoffDate));
            }
            if (month > 0)
            {
                investments = Multiply(investments, investmentFactor) + a.MonthlyInvestmentContribution + redirected;
                savings = Multiply(savings, savingsFactor) + a.MonthlySavingsContribution;
                inflation = Multiply(inflation, inflationFactor);
            }
            var nominal = new ProjectionBalances(investments, savings,
                assets.Sum(asset => Multiply(asset.Value, asset.Input.ConversionRate)),
                assets.Sum(asset => Multiply(asset.Balance, asset.Input.ConversionRate)));
            var adjusted = new ProjectionBalances(investments / inflation, savings / inflation,
                nominal.FinancedAssetValue / inflation, nominal.LoanBalance / inflation);
            // Force derived totals to be checked here, rather than fail later during rendering.
            _ = nominal.NetPosition;
            _ = adjusted.NetPosition;
            months.Add(new(month, date, nominal, adjusted, redirected, loans.AsReadOnly()));
        }
        return new(currency, months.AsReadOnly(), warnings.AsReadOnly());
    }

    // Effective annual growth is preserved across 12 months. Loan APR uses rate / 12 instead.
    private static decimal MonthlyFactor(decimal annualPercent)
    {
        // Preserve the positive remainder near -100%; converting the rate first
        // can round it to exactly -100 in double precision.
        var annualFactor = 1m + annualPercent / 100m;
        var factor = (decimal)Math.Pow((double)annualFactor, 1d / 12);
        if (factor <= 0) throw new OverflowException();
        return factor;
    }

    private static decimal Multiply(decimal balance, decimal factor)
    {
        var result = balance * factor;
        if (balance != 0 && result == 0) throw new OverflowException();
        return result;
    }

    private sealed class AssetState(ProjectionFinancedAssetInput input, DateOnly startDate)
    {
        public ProjectionFinancedAssetInput Input { get; } = input;
        public string Currency { get; } = RequiredAlphabeticCode.Normalize(input.CurrencyCode, 3);
        public decimal Value { get; set; } = input.AssetValue;
        public decimal Balance { get; set; } = input.LoanBalance;
        public decimal GrowthFactor { get; } = MonthlyFactor(input.Assumptions.AnnualAssetAppreciationPercent);
        public DateOnly? PayoffDate { get; set; } = input.LoanBalance == 0 ? startDate : null;
        public bool Warned { get; set; }
    }
}
