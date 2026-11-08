using Hearthcast.Data.Projections;
using Hearthcast.Data.Projections.Calculation;

namespace Hearthcast.Data.Tests.Projections;

public sealed class ProjectionGrowthBreakdownTests
{
    private static readonly DateOnly Start = new(2026, 1, 31);
    private static ProjectionPresetSettings Settings => new("", "GBP", 1, 0, false, [new("GBP", 0, 0, 0, 0)]);
    private static ProjectionFinancedAssetInput Loan(decimal balance = 250, decimal payment = 100,
        decimal interest = 0, decimal growth = 0, decimal value = 1000) =>
        new("GBP", 1, value, balance, new(Guid.NewGuid(), payment, interest, growth, 0, "GBP"));

    private static ProjectionResult Calculate(ProjectionPresetSettings? settings = null, decimal investments = 0,
        decimal savings = 0, params ProjectionFinancedAssetInput[] loans) =>
        ProjectionCalculator.Calculate(settings ?? Settings, new(Start, [new("GBP", investments, savings, 1)], loans));

    private static void Near(decimal expected, decimal actual) => Assert.InRange(actual, expected - .000001m, expected + .000001m);
    private static decimal Changes(ProjectionGrowthBreakdown b) => b.InvestmentReturns + b.SavingsInterest
        + b.FinancedAssetValueChange + b.InvestmentContributions + b.SavingsContributions
        + b.LoanBalanceReduction + b.ReinvestedLoanPayments + b.InflationEffect;

    private static void Reconciles(ProjectionResult result)
    {
        var summary = result.GrowthBreakdown;
        Assert.Equal(result.Months[0].Nominal.NetPosition, summary.Nominal.StartingNetPosition);
        Assert.Equal(summary.Nominal.StartingNetPosition, summary.InflationAdjusted.StartingNetPosition);
        Assert.Equal(result.Months[^1].Nominal.NetPosition, summary.Nominal.FinalNetPosition);
        Assert.Equal(result.Months[^1].InflationAdjusted.NetPosition, summary.InflationAdjusted.FinalNetPosition);
        Near(summary.Nominal.FinalNetPosition, summary.Nominal.StartingNetPosition + Changes(summary.Nominal));
        Near(summary.InflationAdjusted.FinalNetPosition, summary.InflationAdjusted.StartingNetPosition + Changes(summary.InflationAdjusted));
        Assert.Equal(0, summary.Nominal.InflationEffect);
    }

    [Fact]
    public void UnchangedHoldingsHaveNoInventedGrowth()
    {
        var result = Calculate(investments: 100, savings: 200, loans: [Loan(payment: 0)]);
        Reconciles(result);
        Assert.Equal(1050, result.GrowthBreakdown.Nominal.StartingNetPosition);
        Assert.Equal(0, Changes(result.GrowthBreakdown.Nominal));
        Assert.Equal(result.GrowthBreakdown.Nominal, result.GrowthBreakdown.InflationAdjusted);
    }

    [Fact]
    public void RegularContributionsAreSeparateFromStartingWealthAndReturns()
    {
        var result = Calculate(Settings with { Currencies = [new("GBP", 100, 0, 50, 0)] }, 1000, 200);
        Reconciles(result);
        var b = result.GrowthBreakdown.Nominal;
        Assert.Equal(1200, b.StartingNetPosition);
        Assert.Equal(1200, b.InvestmentContributions);
        Assert.Equal(600, b.SavingsContributions);
        Assert.Equal(0, b.InvestmentReturns);
        Assert.Equal(0, b.SavingsInterest);
        Assert.Equal(3000, b.FinalNetPosition);
    }

    [Fact]
    public void ReturnsIncludeGrowthOnNewContributionsWithoutCountingThoseContributionsTwice()
    {
        var result = Calculate(Settings with { Currencies = [new("GBP", 100, 12, 0, 10)] }, 1000, 1000);
        Reconciles(result);
        var factor = Math.Pow(1.12, 1d / 12);
        var finalInvestments = 1120m + 100m * (decimal)((Math.Pow(factor, 12) - 1) / (factor - 1));
        var b = result.GrowthBreakdown.Nominal;
        Near(finalInvestments - 1000 - 1200, b.InvestmentReturns);
        Near(100, b.SavingsInterest);
        Assert.Equal(1200, b.InvestmentContributions);
    }

    [Theory]
    [InlineData(-20, -200)]
    [InlineData(0, 0)]
    [InlineData(12, 120)]
    public void AssetValueChangesAreSeparateFromDebtReduction(int growth, int expectedChange)
    {
        var result = Calculate(loans: [Loan(payment: 0, growth: growth)]);
        Reconciles(result);
        Near(expectedChange, result.GrowthBreakdown.Nominal.FinancedAssetValueChange);
        Assert.Equal(0, result.GrowthBreakdown.Nominal.LoanBalanceReduction);
    }

    [Fact]
    public void InterestIsExcludedFromDebtReductionAndPayoffRemaindersAreCountedOnce()
    {
        var result = Calculate(loans: [Loan(balance: 100, payment: 200, interest: 12)]);
        Reconciles(result);
        var b = result.GrowthBreakdown.Nominal;
        Assert.Equal(1, result.Months[1].FinancedAssets[0].InterestCharged);
        Assert.Equal(101, result.Months[1].FinancedAssets[0].PaymentApplied);
        Assert.Equal(100, b.LoanBalanceReduction);
        Assert.Equal(2299, b.ReinvestedLoanPayments); // 99 in payoff month, then eleven payments of 200.
        Assert.Equal(0, b.InvestmentReturns);
        Assert.Equal(3299, b.FinalNetPosition);
    }

    [Fact]
    public void GrowthOnReinvestedPaymentsBelongsToReturns()
    {
        var result = Calculate(Settings with { Currencies = [new("GBP", 0, 12, 0, 0)] }, loans: [Loan()]);
        Reconciles(result);
        var b = result.GrowthBreakdown.Nominal;
        Assert.Equal(950, b.ReinvestedLoanPayments);
        Assert.True(b.InvestmentReturns > 0);
        Near(result.Months[^1].Nominal.Investments - 950, b.InvestmentReturns);
        Assert.Equal(250, b.LoanBalanceReduction);
        Assert.Equal(0, b.InvestmentContributions);
    }

    [Fact]
    public void IndependentPayoffsAndAlreadyPaidLoansKeepTheirOwnCashFlows()
    {
        var result = Calculate(loans: [Loan(100), Loan(250), Loan(0, 20)]);
        Reconciles(result);
        var b = result.GrowthBreakdown.Nominal;
        Assert.Equal(350, b.LoanBalanceReduction);
        Assert.Equal(1100 + 950 + 240, b.ReinvestedLoanPayments);
        Assert.Equal(Start, result.Months[^1].FinancedAssets[2].PayoffDate);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(-10)]
    public void RealContributionsAreValuedWhenMadeAndInflationIsASeparateEffect(int inflation)
    {
        var settings = Settings with { DurationYears = 2, AnnualInflationPercent = inflation,
            Currencies = [new("GBP", 100, inflation, 50, inflation)] };
        var result = Calculate(settings, 1000, 500);
        Reconciles(result);
        var b = result.GrowthBreakdown.InflationAdjusted;
        Near(2400, b.InvestmentContributions);
        Near(1200, b.SavingsContributions);
        Near(5100, b.FinalNetPosition);
        Near(0, b.InvestmentReturns + b.SavingsInterest + b.InflationEffect);
        Assert.Equal(-Math.Sign(inflation), Math.Sign(b.InflationEffect));
        var toggled = Calculate(settings with { ShowInflationAdjustedValues = true }, 1000, 500);
        Assert.Equal(result.GrowthBreakdown, toggled.GrowthBreakdown);
    }

    [Fact]
    public void NegativeReturnsAndGrowingDebtRemainNegativeComponents()
    {
        var result = Calculate(Settings with { AnnualInflationPercent = 3, Currencies = [new("GBP", 0, -20, 0, -10)] },
            100, 100, Loan(balance: 1000, payment: 0, interest: 12, value: 100));
        Reconciles(result);
        var b = result.GrowthBreakdown.Nominal;
        Near(-20, b.InvestmentReturns);
        Near(-10, b.SavingsInterest);
        Near(1000 - 1000 * (decimal)Math.Pow(1.01, 12), b.LoanBalanceReduction);
        Assert.Equal(0, b.ReinvestedLoanPayments);
        Assert.True(result.GrowthBreakdown.InflationAdjusted.InflationEffect > 0); // Negative net wealth loses real debt value.
    }

    [Fact]
    public void ChangingViewingCurrencyScalesEveryComponentWithoutChangingNativeCashFlows()
    {
        var id = Guid.NewGuid();
        ProjectionResult Run(bool pounds)
        {
            var settings = new ProjectionPresetSettings("", pounds ? "GBP" : "ZAR", 2, 3, false,
                [new("GBP", 100, 8, 50, 2), new("ZAR", 500, 5, 100, 4)]);
            return ProjectionCalculator.Calculate(settings, new(Start,
                [new("GBP", 1000, 100, pounds ? 1 : 20), new("ZAR", 5000, 200, pounds ? .05m : 1)],
                [new("ZAR", pounds ? .05m : 1, 10000, 1000, new(id, 500, 12, 4.5m, 200, "GBP"))]));
        }
        var gbp = Run(true);
        var zar = Run(false);
        Reconciles(gbp);
        Reconciles(zar);
        Assert.Equal(gbp.Months[^1].Currencies, zar.Months[^1].Currencies);
        Assert.Equal(gbp.Months[^1].FinancedAssets, zar.Months[^1].FinancedAssets);
        static decimal[] Values(ProjectionGrowthBreakdown b) => [b.StartingNetPosition, b.InvestmentReturns,
            b.SavingsInterest, b.FinancedAssetValueChange, b.InvestmentContributions, b.SavingsContributions,
            b.LoanBalanceReduction, b.ReinvestedLoanPayments, b.InflationEffect, b.FinalNetPosition];
        foreach (var pair in new[] { (gbp.GrowthBreakdown.Nominal, zar.GrowthBreakdown.Nominal),
                     (gbp.GrowthBreakdown.InflationAdjusted, zar.GrowthBreakdown.InflationAdjusted) })
            foreach (var values in Values(pair.Item1).Zip(Values(pair.Item2)))
                Near(values.First * 20, values.Second);
    }
}
