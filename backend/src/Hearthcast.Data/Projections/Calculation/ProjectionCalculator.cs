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
        assumptions = ProjectionRules.Normalize(assumptions);
        starting = starting with { Currencies = starting.Currencies?.Select(c => c is null
            ? throw new InvalidValueException("Currency positions cannot be null.")
            : c with { CurrencyCode = RequiredAlphabeticCode.Normalize(c.CurrencyCode, 3) }).ToArray()!,
            FinancedAssets = starting.FinancedAssets.Select(a => a is null || a.Assumptions is null
                ? throw new InvalidValueException("Financed asset inputs cannot be null.")
                : a with { Assumptions = ProjectionRules.NormalizeLoan(a.Assumptions) }).ToArray() };
        var currency = assumptions.CurrencyCode;
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
        if (starting.Currencies is null || starting.Currencies.Count != a.Currencies.Count
            || starting.Currencies.Select(c => c.CurrencyCode).Distinct().Count() != a.Currencies.Count
            || starting.Currencies.Any(c => !a.Currencies.Any(s => s.CurrencyCode == c.CurrencyCode)))
            throw new InvalidValueException("Supply one known starting position for each configured currency pot.");
        ProjectionRules.ValidateDestinations(starting.FinancedAssets.Select(x => x.Assumptions), a.Currencies);
        foreach (var pot in starting.Currencies)
        {
            ProjectionRules.Contribution(pot.Investments, nameof(pot.Investments));
            ProjectionRules.Contribution(pot.Savings, nameof(pot.Savings));
            var settings = a.Currencies.Single(c => c.CurrencyCode == pot.CurrencyCode);
            var used = pot.Investments != 0 || pot.Savings != 0 || settings.MonthlyInvestmentContribution != 0
                || settings.MonthlySavingsContribution != 0 || starting.FinancedAssets.Any(l => l.Assumptions.InvestmentCurrencyAfterPayoff == pot.CurrencyCode
                    && (l.Assumptions.MonthlyLoanPaymentInAssetCurrency != 0 || l.Assumptions.ExtraMonthlyLoanPaymentInAssetCurrency != 0));
            if ((used && pot.ConversionRate is null) || pot.ConversionRate <= 0 || (pot.CurrencyCode == currency && pot.ConversionRate != 1))
                throw new InvalidValueException("Currency pots require a positive conversion rate, or 1 for the viewing currency.");
        }
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
        var pots = a.Currencies.ToDictionary(c => c.CurrencyCode,
            c => new CurrencyState(c, starting.Currencies.Single(p => p.CurrencyCode == c.CurrencyCode)));
        var inflationFactor = MonthlyFactor(a.AnnualInflationPercent);
        var assets = starting.FinancedAssets.Select(asset => new AssetState(asset, starting.Date)).ToArray();
        var months = new List<ProjectionMonth>();
        var warnings = new List<ProjectionWarning>();
        var growth = new ProjectionGrowthAccumulator();
        var expenseTarget = a.ExpenseTarget is { } target ? new ProjectionExpenseTargetTracker(target, currency,
            starting.ExpenseCurrencyConversionRate, starting.Currencies) : null;

        var inflation = 1m;
        for (var month = 0; month <= a.DurationYears * 12; month++)
        {
            // Anchor each date to the original day (Jan 31 -> Feb 28 -> Mar 31).
            var date = starting.Date.AddMonths(month);
            foreach (var pot in pots.Values) pot.Redirected = 0;
            var redirected = 0m;
            var investmentContributions = 0m;
            var savingsContributions = 0m;
            var previousInflation = inflation;
            var loans = new List<ProjectionLoanMonth>();
            foreach (var asset in assets)
            {
                var interest = 0m;
                var payment = 0m;
                var surplus = 0m;
                var destinationAmount = 0m;
                if (month > 0)
                {
                    asset.Value = Multiply(asset.Value, asset.GrowthFactor);
                    interest = asset.Balance * asset.Input.Assumptions.AnnualLoanInterestPercent / 1200m;
                    var budget = asset.Input.Assumptions.MonthlyLoanPaymentInAssetCurrency + asset.Input.Assumptions.ExtraMonthlyLoanPaymentInAssetCurrency;
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
                    var destination = pots[asset.Input.Assumptions.InvestmentCurrencyAfterPayoff];
                    if (surplus != 0)
                    {
                        var transferRate = asset.Input.ConversionRate / destination.Position.ConversionRate!.Value;
                        if (transferRate <= 0) throw new OverflowException();
                        destinationAmount = Multiply(surplus, transferRate);
                    }
                    destination.Redirected += destinationAmount;
                    redirected += Convert(destinationAmount, destination.Position.ConversionRate);
                }
                loans.Add(new(asset.Input.Assumptions.FinancedAssetId, asset.Currency, asset.Value,
                    asset.Balance, interest, payment, surplus, asset.PayoffDate,
                    asset.Input.Assumptions.InvestmentCurrencyAfterPayoff, destinationAmount));
            }
            if (month > 0)
            {
                inflation = Multiply(inflation, inflationFactor);
                foreach (var pot in pots.Values)
                {
                    // Entered contributions are in starting-snapshot money, preserving their real value.
                    var investmentContribution = Multiply(pot.Settings.MonthlyInvestmentContribution, inflation);
                    var savingsContribution = Multiply(pot.Settings.MonthlySavingsContribution, inflation);
                    pot.Investments = Multiply(pot.Investments, pot.InvestmentFactor)
                        + investmentContribution + pot.Redirected;
                    pot.Savings = Multiply(pot.Savings, pot.SavingsFactor)
                        + savingsContribution;
                    investmentContributions += Convert(investmentContribution, pot.Position.ConversionRate);
                    savingsContributions += Convert(savingsContribution, pot.Position.ConversionRate);
                }
            }
            var nominal = new ProjectionBalances(pots.Values.Sum(p => Convert(p.Investments, p.Position.ConversionRate)),
                pots.Values.Sum(p => Convert(p.Savings, p.Position.ConversionRate)),
                assets.Sum(asset => Multiply(asset.Value, asset.Input.ConversionRate)),
                assets.Sum(asset => Multiply(asset.Balance, asset.Input.ConversionRate)));
            var adjusted = new ProjectionBalances(nominal.Investments / inflation, nominal.Savings / inflation,
                nominal.FinancedAssetValue / inflation, nominal.LoanBalance / inflation);
            // Force derived totals to be checked here, rather than fail later during rendering.
            _ = nominal.NetPosition;
            _ = adjusted.NetPosition;
            if (month > 0)
                growth.Add(months[^1].Nominal, nominal, investmentContributions, savingsContributions,
                    redirected, previousInflation, inflation);
            var nativeMonths = Array.AsReadOnly(pots.Values.OrderBy(p => p.Settings.CurrencyCode)
                .Select(p => new ProjectionCurrencyMonth(p.Settings.CurrencyCode, p.Investments, p.Savings, p.Redirected)).ToArray());
            months.Add(new(month, date, nominal, adjusted, redirected, loans.AsReadOnly(), nativeMonths,
                expenseTarget?.Record(date, nativeMonths, nominal, adjusted, inflation)));
        }
        return new(currency, months.AsReadOnly(), warnings.AsReadOnly(),
            growth.Finish(months[0].Nominal.NetPosition, months[^1].Nominal.NetPosition, months[^1].InflationAdjusted.NetPosition),
            expenseTarget?.Finish(starting.Date));
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

    private static decimal Convert(decimal amount, decimal? rate) => amount == 0 ? 0
        : Multiply(amount, rate ?? throw new InvalidValueException("A nonzero currency balance requires a known conversion rate."));

    private static decimal Multiply(decimal balance, decimal factor)
    {
        var result = balance * factor;
        if (balance != 0 && result == 0) throw new OverflowException();
        return result;
    }

    private sealed class CurrencyState(ProjectionCurrencySettings settings, ProjectionCurrencyPosition position)
    {
        public ProjectionCurrencySettings Settings { get; } = settings;
        public ProjectionCurrencyPosition Position { get; } = position;
        public decimal Investments { get; set; } = position.Investments;
        public decimal Savings { get; set; } = position.Savings;
        public decimal InvestmentFactor { get; } = MonthlyFactor(settings.AnnualInvestmentReturnPercent);
        public decimal SavingsFactor { get; } = MonthlyFactor(settings.AnnualSavingsInterestPercent);
        public decimal Redirected { get; set; }
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
