using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections.Calculation;

internal sealed class ProjectionExpenseTargetTracker
{
    private readonly ProjectionExpenseTargetSettings _settings;
    private readonly decimal _expenseRate;
    private readonly decimal _requiredCapital;
    private readonly decimal _requiredCapitalInView;
    private readonly Dictionary<string, decimal?> _quotes;
    private DateOnly? _firstReachedDate;

    public ProjectionExpenseTargetTracker(ProjectionExpenseTargetSettings settings, string viewingCurrency,
        decimal? expenseRate, IReadOnlyList<ProjectionCurrencyPosition> currencies)
    {
        _settings = settings;
        var rate = expenseRate ?? (settings.CurrencyCode == viewingCurrency ? 1m : (decimal?)null);
        if (rate is null || rate <= 0 || (settings.CurrencyCode == viewingCurrency && rate != 1))
            throw new InvalidValueException("The expense currency needs a positive saved conversion rate, or 1 for the viewing currency.");
        _expenseRate = rate.Value;
        var withdrawalFraction = settings.AnnualWithdrawalRatePercent / 100m;
        if (withdrawalFraction == 0) throw new OverflowException();
        _requiredCapital = settings.AnnualExpenses / withdrawalFraction;
        if (_requiredCapital <= 0) throw new OverflowException();
        _requiredCapitalInView = Scale(_requiredCapital, _expenseRate);
        _quotes = currencies.ToDictionary(c => c.CurrencyCode, c => c.ConversionRate);
    }

    public ProjectionExpenseTargetMonth Record(DateOnly date, IReadOnlyList<ProjectionCurrencyMonth> currencies,
        ProjectionBalances nominal, ProjectionBalances adjusted, decimal inflation)
    {
        var eligibleInExpenseCurrency = 0m;
        foreach (var pot in currencies)
        {
            var balance = pot.Investments + (_settings.IncludeSavings ? pot.Savings : 0);
            if (balance == 0) continue;
            var rate = (_quotes[pot.CurrencyCode] ?? throw new InvalidValueException("The eligible portfolio needs a saved conversion rate.")) / _expenseRate;
            if (rate <= 0) throw new OverflowException();
            eligibleInExpenseCurrency += Scale(balance, rate);
        }
        // Determine crossing in the fixed expense currency, independent of chart units/display.
        // Absorb tiny FX/compounding remnants without rounding a positive target to zero.
        // Limit tolerance to half a cent and one trillionth of the required capital.
        var required = Scale(_requiredCapital, inflation);
        var tolerance = Math.Min(0.005m, required / 1_000_000_000_000m);
        var covered = eligibleInExpenseCurrency >= required
            || required - eligibleInExpenseCurrency <= tolerance;
        if (covered) _firstReachedDate ??= date;
        return new(Scale(_requiredCapitalInView, inflation), _requiredCapitalInView,
            nominal.Investments + (_settings.IncludeSavings ? nominal.Savings : 0),
            adjusted.Investments + (_settings.IncludeSavings ? adjusted.Savings : 0), covered);
    }

    public ProjectionExpenseTargetSummary Finish(DateOnly start) => new(_settings.CurrencyCode, _settings.AnnualExpenses,
        _settings.AnnualWithdrawalRatePercent, _settings.IncludeSavings, _requiredCapital,
        _firstReachedDate, _firstReachedDate == start);

    private static decimal Scale(decimal value, decimal factor)
    {
        var result = value * factor;
        if (value != 0 && result == 0) throw new OverflowException();
        return result;
    }
}
