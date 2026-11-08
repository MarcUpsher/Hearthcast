using Hearthcast.Data.Projections;
using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Tests.Projections;

public sealed class ProjectionExpenseTargetTests
{
    private static readonly DateOnly Start = new(2026, 1, 31);
    private static ProjectionPresetSettings Settings(decimal expenses = 1200, decimal withdrawal = 4,
        bool savings = false, decimal inflation = 0) => new("", "GBP", 2, inflation, false,
        [new("GBP", 0, 0, 0, 0)], new(expenses, "GBP", withdrawal, savings));
    private static ProjectionStartingPosition Starting(decimal investments = 0, decimal savings = 0) =>
        new(Start, [new("GBP", investments, savings, 1)], []);
    private static void Near(decimal expected, decimal actual) => Assert.InRange(actual, expected - .000001m, expected + .000001m);

    [Theory]
    [InlineData(3, 1333333.333333)]
    [InlineData(4, 1000000)]
    [InlineData(5, 800000)]
    public void AnnualExpensesAndWithdrawalRateDefineCapitalInTheFixedExpenseCurrency(int rate, double expected)
    {
        var result = ProjectionCalculator.Calculate(Settings(40000, rate), Starting());
        Near((decimal)expected, result.ExpenseTarget!.RequiredCapitalInExpenseCurrency);
        Assert.Equal("GBP", result.ExpenseTarget.ExpenseCurrencyCode);
        Assert.Null(result.ExpenseTarget.FirstReachedDate);
        Assert.All(result.Months, m => Assert.False(m.ExpenseTarget!.IsCovered));
    }

    [Fact]
    public void FirstCrossingUsesTheFirstMonthlyEligibleBalanceAndPreservesAnchoredDates()
    {
        var settings = Settings() with { Currencies = [new("GBP", 1000, 0, 0, 0)] };
        var result = ProjectionCalculator.Calculate(settings, Starting(28000));
        Assert.Equal(Start.AddMonths(2), result.ExpenseTarget!.FirstReachedDate);
        Assert.False(result.ExpenseTarget.AlreadyReachedAtStart);
        Assert.False(result.Months[1].ExpenseTarget!.IsCovered);
        Assert.True(result.Months[2].ExpenseTarget!.IsCovered);
        Assert.Equal(30000, result.Months[2].ExpenseTarget!.NominalEligibleBalance);
    }

    [Fact]
    public void SavingsAreOptionalAndPropertyEquityNeverFundsThisTarget()
    {
        var starting = Starting(29000, 2000) with { FinancedAssets = [new("GBP", 1, 2_000_000, 0,
            new(Guid.NewGuid(), 0, 0, 0, 0, "GBP"))] };
        var investments = ProjectionCalculator.Calculate(Settings(), starting);
        Assert.Null(investments.ExpenseTarget!.FirstReachedDate);
        Assert.Equal(29000, investments.Months[0].ExpenseTarget!.NominalEligibleBalance);
        var withSavings = ProjectionCalculator.Calculate(Settings(savings: true), starting);
        Assert.True(withSavings.ExpenseTarget!.AlreadyReachedAtStart);
        Assert.Equal(Start, withSavings.ExpenseTarget.FirstReachedDate);
        Assert.Equal(31000, withSavings.Months[0].ExpenseTarget!.NominalEligibleBalance);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(-10)]
    public void RealTargetStaysConstantAndMatchingInflationGrowthDoesNotFlickerAtTheBoundary(int inflation)
    {
        var settings = Settings(inflation: inflation) with { Currencies = [new("GBP", 0, inflation, 0, 0)] };
        var result = ProjectionCalculator.Calculate(settings, Starting(30000));
        Assert.True(result.ExpenseTarget!.AlreadyReachedAtStart);
        foreach (var month in result.Months)
        {
            Assert.Equal(30000, month.ExpenseTarget!.InflationAdjustedRequiredCapital);
            Near(month.Nominal.Investments, month.ExpenseTarget.NominalRequiredCapital);
            Assert.True(month.ExpenseTarget.IsCovered);
        }
        var toggled = ProjectionCalculator.Calculate(settings with { ShowInflationAdjustedValues = true }, Starting(30000));
        Assert.Equal(result.ExpenseTarget, toggled.ExpenseTarget);
        Assert.Equal(result.Months.Select(m => m.ExpenseTarget), toggled.Months.Select(m => m.ExpenseTarget));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyPortfolioNeverMeetsAPositiveTargetDuringLongDeflation(bool adjusted)
    {
        var settings = Settings(inflation: -50) with { DurationYears = 30, ShowInflationAdjustedValues = adjusted };
        var result = ProjectionCalculator.Calculate(settings, Starting());
        Assert.InRange(result.Months[^1].ExpenseTarget!.NominalRequiredCapital, 0.000001m, 0.005m);
        Assert.Equal(30000, result.Months[^1].ExpenseTarget!.InflationAdjustedRequiredCapital);
        Assert.Null(result.ExpenseTarget!.FirstReachedDate);
        Assert.False(result.ExpenseTarget.AlreadyReachedAtStart);
        Assert.All(result.Months, m => Assert.False(m.ExpenseTarget!.IsCovered));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.0001, false)]
    [InlineData(0.003999, false)]
    [InlineData(0.004, true)]
    public void PositiveSubCentTargetRequiresEnoughCapital(double investment, bool covered)
    {
        var result = ProjectionCalculator.Calculate(Settings(expenses: 0.00016m), Starting((decimal)investment));
        Assert.Equal(0.004m, result.ExpenseTarget!.RequiredCapitalInExpenseCurrency);
        Assert.Equal(covered ? Start : (DateOnly?)null, result.ExpenseTarget.FirstReachedDate);
        Assert.Equal(covered, result.ExpenseTarget.AlreadyReachedAtStart);
        Assert.All(result.Months, m => Assert.Equal(covered, m.ExpenseTarget!.IsCovered));
    }

    [Fact]
    public void FirstReachedIsRetainedWhenThePortfolioLaterFallsBelowTarget()
    {
        var result = ProjectionCalculator.Calculate(Settings() with { Currencies = [new("GBP", 0, -20, 0, 0)] }, Starting(31000));
        Assert.Equal(Start, result.ExpenseTarget!.FirstReachedDate);
        Assert.False(result.Months[^1].ExpenseTarget!.IsCovered);
    }

    [Fact]
    public void EnablingTheIndicatorDoesNotChangeBalancesCashFlowsPayoffOrGrowthBreakdown()
    {
        var starting = Starting(10000) with { FinancedAssets = [new("GBP", 1, 20000, 1000,
            new(Guid.NewGuid(), 200, 9, 3, 50, "GBP"))] };
        var enabled = Settings(inflation: 3) with { Currencies = [new("GBP", 100, 8, 50, 2)] };
        var before = ProjectionCalculator.Calculate(enabled with { ExpenseTarget = null }, starting);
        var after = ProjectionCalculator.Calculate(enabled, starting);
        Assert.Null(before.ExpenseTarget);
        Assert.All(before.Months, m => Assert.Null(m.ExpenseTarget));
        Assert.Equal(before.GrowthBreakdown, after.GrowthBreakdown);
        for (var i = 0; i < before.Months.Count; i++)
        {
            Assert.Equal(before.Months[i].Nominal, after.Months[i].Nominal);
            Assert.Equal(before.Months[i].InflationAdjusted, after.Months[i].InflationAdjusted);
            Assert.Equal(before.Months[i].FinancedAssets, after.Months[i].FinancedAssets);
            Assert.Equal(before.Months[i].Currencies, after.Months[i].Currencies);
        }
    }

    [Fact]
    public void ForeignExpenseCurrencyCrossingIsIndependentOfViewingCurrency()
    {
        ProjectionResult Run(bool pounds)
        {
            var settings = Settings(1200) with { CurrencyCode = pounds ? "GBP" : "ZAR",
                Currencies = [new("ZAR", 20000, 0, 0, 0)] };
            return ProjectionCalculator.Calculate(settings, new(Start,
                [new("ZAR", 560000, 0, pounds ? .05m : 1)], [], pounds ? 1 : 20));
        }
        var gbp = Run(true);
        var zar = Run(false);
        Assert.Equal(gbp.ExpenseTarget, zar.ExpenseTarget);
        Assert.Equal(Start.AddMonths(2), zar.ExpenseTarget!.FirstReachedDate);
        for (var i = 0; i < gbp.Months.Count; i++)
        {
            Assert.Equal(gbp.Months[i].ExpenseTarget!.IsCovered, zar.Months[i].ExpenseTarget!.IsCovered);
            Near(gbp.Months[i].ExpenseTarget!.NominalRequiredCapital * 20, zar.Months[i].ExpenseTarget!.NominalRequiredCapital);
            Near(gbp.Months[i].ExpenseTarget!.NominalEligibleBalance * 20, zar.Months[i].ExpenseTarget!.NominalEligibleBalance);
        }
    }

    [Theory]
    [InlineData("ExpensesZero")]
    [InlineData("ExpensesNegative")]
    [InlineData("WithdrawalZero")]
    [InlineData("WithdrawalNegative")]
    [InlineData("WithdrawalHigh")]
    [InlineData("Currency")]
    [InlineData("MissingRate")]
    [InlineData("RateZero")]
    [InlineData("IdentityRate")]
    public void InvalidTargetsAndMissingQuotesAreRejected(string problem)
    {
        var target = new ProjectionExpenseTargetSettings(problem == "ExpensesZero" ? 0 : problem == "ExpensesNegative" ? -1 : 1200,
            problem == "Currency" ? "12!" : problem == "MissingRate" ? "EUR" : "GBP",
            problem == "WithdrawalZero" ? 0 : problem == "WithdrawalNegative" ? -1 : problem == "WithdrawalHigh" ? 101 : 4, false);
        var starting = Starting() with { ExpenseCurrencyConversionRate = problem == "RateZero" ? 0 : problem == "IdentityRate" ? 2 : null };
        Assert.ThrowsAny<ArgumentException>(() => ProjectionCalculator.Calculate(Settings() with { ExpenseTarget = target }, starting));
    }

    [Fact]
    public void UnsupportedCapitalOrConversionPrecisionReturnsTheExistingNumericLimitsError()
    {
        foreach (var target in new[] { new ProjectionExpenseTargetSettings(decimal.MaxValue, "GBP", .01m, false),
                     new ProjectionExpenseTargetSettings(1, "GBP", .0000000000000000000000000001m, false) })
        {
            var failure = Assert.Throws<InvalidValueException>(() => ProjectionCalculator.Calculate(Settings() with { ExpenseTarget = target }, Starting()));
            Assert.Contains("numeric limits", failure.Message);
        }
    }
}
