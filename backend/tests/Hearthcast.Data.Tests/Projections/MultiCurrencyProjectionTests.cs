using Hearthcast.Data.Projections;
using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Tests.Projections;

public sealed class MultiCurrencyProjectionTests
{
    private static readonly Guid LoanId = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static ProjectionPresetSettings Settings(string view = "GBP") => new("", view, 10, 0, false,
        [new("GBP", 1000, 0, 20, 0), new("ZAR", 500, 0, 100, 0)]);
    private static ProjectionStartingPosition Starting(string view = "GBP", decimal extra = 2000, string destination = "GBP") => new(Start,
        [new("GBP", 10_000, 200, view == "GBP" ? 1 : 20), new("ZAR", 50_000, 1000, view == "GBP" ? .05m : 1)],
        [new("ZAR", view == "GBP" ? .05m : 1, 1_200_000, 1_000_000, new(LoanId, 20_000, 12, 0, extra, destination))]);

    [Fact]
    public void SimultaneousContributionsAndRegularPlusExtraPaymentsRemainInTheirNativeCurrencies()
    {
        var result = ProjectionCalculator.Calculate(Settings(), Starting());
        var month = result.Months[1];
        var gbp = month.Currencies.Single(c => c.CurrencyCode == "GBP");
        var zar = month.Currencies.Single(c => c.CurrencyCode == "ZAR");
        Assert.Equal(11_000, gbp.Investments);
        Assert.Equal(220, gbp.Savings);
        Assert.Equal(50_500, zar.Investments);
        Assert.Equal(1100, zar.Savings);
        var loan = Assert.Single(month.FinancedAssets);
        Assert.Equal(10_000, loan.InterestCharged);
        Assert.Equal(22_000, loan.PaymentApplied);
        Assert.Equal(988_000, loan.LoanBalance);
        Assert.Equal(0, month.RedirectedToInvestments);
        Assert.Equal(13_525, month.Nominal.Investments);
    }

    [Fact]
    public void ExtraPaymentAcceleratesPayoffAndBothBudgetsAreRedirectedIntoTheChosenPot()
    {
        var normal = ProjectionCalculator.Calculate(Settings(), Starting(extra: 0));
        var extra = ProjectionCalculator.Calculate(Settings(), Starting());
        var payoff = extra.Months.First(m => m.Month > 0 && m.FinancedAssets[0].LoanBalance == 0);
        Assert.True(payoff.Date < normal.Months[^1].FinancedAssets[0].PayoffDate);
        Assert.InRange(payoff.FinancedAssets[0].PaymentApplied, 0, 22_000);
        Assert.Equal((22_000 - payoff.FinancedAssets[0].PaymentApplied) / 20, payoff.Currencies.Single(c => c.CurrencyCode == "GBP").RedirectedToInvestments);
        var after = extra.Months[payoff.Month + 1];
        Assert.Equal(1100, after.Currencies.Single(c => c.CurrencyCode == "GBP").RedirectedToInvestments);
        Assert.Equal(2100, after.Currencies.Single(c => c.CurrencyCode == "GBP").Investments - payoff.Currencies.Single(c => c.CurrencyCode == "GBP").Investments);
        Assert.Equal(500, after.Currencies.Single(c => c.CurrencyCode == "ZAR").Investments - payoff.Currencies.Single(c => c.CurrencyCode == "ZAR").Investments);
        Assert.Equal("GBP", after.FinancedAssets[0].InvestmentCurrencyAfterPayoff);
        Assert.Equal(1100, after.FinancedAssets[0].InvestmentAmountRedirected);
    }

    [Fact]
    public void ChangingTheViewingCurrencyDoesNotChangeNativeBalancesCashFlowsOrPayoffDates()
    {
        var gbp = ProjectionCalculator.Calculate(Settings(), Starting());
        var zar = ProjectionCalculator.Calculate(Settings("ZAR"), Starting("ZAR"));
        for (var i = 0; i < gbp.Months.Count; i++)
        {
            Assert.Equal(gbp.Months[i].Currencies, zar.Months[i].Currencies);
            Assert.Equal(gbp.Months[i].FinancedAssets, zar.Months[i].FinancedAssets);
            Assert.InRange(zar.Months[i].Nominal.NetPosition - gbp.Months[i].Nominal.NetPosition * 20, -.000001m, .000001m);
        }
    }

    [Fact]
    public void DestinationCanBeTheNativeInvestmentPotInsteadOfTheViewingCurrency()
    {
        var result = ProjectionCalculator.Calculate(Settings(), Starting(destination: "ZAR"));
        var final = result.Months[^1];
        Assert.Equal(22_000, final.Currencies.Single(c => c.CurrencyCode == "ZAR").RedirectedToInvestments);
        Assert.Equal(0, final.Currencies.Single(c => c.CurrencyCode == "GBP").RedirectedToInvestments);
        Assert.Equal(1100, final.RedirectedToInvestments);
    }

    [Fact]
    public void EachPotHasItsOwnNominalGrowthRate()
    {
        var settings = new ProjectionPresetSettings("", "GBP", 1, 0, false,
            [new("GBP", 0, 10, 0, 2), new("ZAR", 0, 20, 0, 5)]);
        var starting = new ProjectionStartingPosition(Start, [new("GBP", 1000, 1000, 1), new("ZAR", 1000, 1000, .05m)], []);
        var month = ProjectionCalculator.Calculate(settings, starting).Months[^1];
        Assert.InRange(month.Currencies.Single(c => c.CurrencyCode == "GBP").Investments, 1099.999999m, 1100.000001m);
        Assert.InRange(month.Currencies.Single(c => c.CurrencyCode == "ZAR").Investments, 1199.999999m, 1200.000001m);
        Assert.InRange(month.Currencies.Single(c => c.CurrencyCode == "GBP").Savings, 1019.999999m, 1020.000001m);
        Assert.InRange(month.Currencies.Single(c => c.CurrencyCode == "ZAR").Savings, 1049.999999m, 1050.000001m);
    }

    [Fact]
    public void InvalidCurrencySetsUnknownDestinationsAndNegativeExtraPaymentsAreRejected()
    {
        Assert.Throws<InvalidValueException>(() => ProjectionCalculator.Calculate(Settings() with { Currencies = [] }, Starting()));
        Assert.Throws<InvalidValueException>(() => ProjectionCalculator.Calculate(Settings() with { Currencies = [Settings().Currencies[0], Settings().Currencies[0] with { CurrencyCode = " gbp " }] }, Starting()));
        Assert.Throws<InvalidValueException>(() => ProjectionCalculator.Calculate(Settings(), Starting(destination: "USD")));
        Assert.Throws<InvalidValueException>(() => ProjectionCalculator.Calculate(Settings(), Starting(extra: -1)));
        Assert.Throws<InvalidValueException>(() => ProjectionCalculator.Calculate(Settings(), Starting() with { Currencies = [Starting().Currencies[0]] }));
    }
}
