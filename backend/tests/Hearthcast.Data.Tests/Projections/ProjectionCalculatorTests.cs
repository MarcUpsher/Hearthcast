using Hearthcast.Data.Projections;
using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Tests.Projections;

public sealed class ProjectionCalculatorTests
{
    private static readonly DateOnly Start = new(2024, 1, 31);
    private static ProjectionPresetSettings Assumptions => new("", "GBP", 1, 0, false, [new("GBP", 0, 0, 0, 0)]);
    private static ProjectionFinancedAssetInput Loan(decimal balance = 250, decimal payment = 100,
        decimal interest = 0, decimal growth = 0, decimal value = 1000, string currency = "GBP", decimal fx = 1) =>
        new(currency, fx, value, balance, new(Guid.NewGuid(), payment, interest, growth, 0, "GBP"));
    private static ProjectionResult Calculate(ProjectionPresetSettings? assumptions = null, decimal investments = 0,
        decimal savings = 0, params ProjectionFinancedAssetInput[] loans) =>
        ProjectionCalculator.Calculate(assumptions ?? Assumptions, new(Start, [new("GBP", investments, savings, 1)], loans));
    private static void Near(decimal expected, decimal actual) => Assert.InRange(actual, expected - .000001m, expected + .000001m);

    [Fact]
    public void IncludesUntouchedStartingPointAndUsesAnchoredMonthEndDates()
    {
        var result = Calculate(investments: 100, savings: 200, loans: [Loan()]);
        Assert.Equal(13, result.Months.Count);
        var initial = result.Months[0];
        Assert.Equal(Start, initial.Date);
        Assert.Equal(new ProjectionBalances(100, 200, 1000, 250), initial.Nominal);
        Assert.Equal(initial.Nominal, initial.InflationAdjusted);
        Assert.Equal(0, initial.RedirectedToInvestments);
        Assert.Equal(0, initial.FinancedAssets[0].PaymentApplied);
        Assert.Equal(new DateOnly(2024, 2, 29), result.Months[1].Date);
        Assert.Equal(new DateOnly(2024, 3, 31), result.Months[2].Date);
    }

    [Fact]
    public void EndOfMonthContributionsAccumulateWithoutGrowth()
    {
        var result = Calculate(Assumptions with { Currencies = [Assumptions.Currencies[0] with { MonthlyInvestmentContribution = 100, MonthlySavingsContribution = 50 }] }, 1000, 200);
        Assert.Equal(1100, result.Months[1].Nominal.Investments);
        Assert.Equal(2200, result.Months[^1].Nominal.Investments);
        Assert.Equal(800, result.Months[^1].Nominal.Savings);
        Assert.Equal(3000, result.Months[^1].Nominal.NetPosition);
    }

    [Theory]
    [InlineData(12, 1120)]
    [InlineData(-20, 800)]
    [InlineData(0, 1000)]
    public void AnnualGrowthCompoundsToSpecifiedAnnualReturn(int annualRate, int final)
    {
        var a = Assumptions with { Currencies = [Assumptions.Currencies[0] with { AnnualInvestmentReturnPercent = annualRate, AnnualSavingsInterestPercent = annualRate }] };
        var result = Calculate(a, 1000, 1000, Loan(balance: 0, payment: 0, growth: annualRate));
        Near(final, result.Months[^1].Nominal.Investments);
        Near(final, result.Months[^1].Nominal.Savings);
        Near(final, result.Months[^1].Nominal.FinancedAssetValue);
    }

    [Fact]
    public void NewContributionsDoNotReceiveGrowthForTheMonthTheyAreAdded()
    {
        var result = Calculate(Assumptions with { Currencies = [Assumptions.Currencies[0] with { MonthlyInvestmentContribution = 100, AnnualInvestmentReturnPercent = 12 }] });
        Assert.Equal(100, result.Months[1].Nominal.Investments);
        Near(100 * (decimal)Math.Pow(1.12, 1d / 12) + 100, result.Months[2].Nominal.Investments);
    }

    [Fact]
    public void RedirectsFinalPaymentRemainderAndAllSubsequentPayments()
    {
        var result = Calculate(Assumptions with { Currencies = [Assumptions.Currencies[0] with { MonthlyInvestmentContribution = 10 }] }, loans: [Loan()]);
        Assert.Equal(150, result.Months[1].Nominal.LoanBalance);
        Assert.Equal(50, result.Months[2].Nominal.LoanBalance);
        var payoff = result.Months[3];
        Assert.Equal(0, payoff.Nominal.LoanBalance);
        Assert.Equal(50, payoff.RedirectedToInvestments);
        Assert.Equal(80, payoff.Nominal.Investments);
        Assert.Equal(payoff.Date, payoff.FinancedAssets[0].PayoffDate);
        Assert.Equal(50, payoff.FinancedAssets[0].PaymentApplied);
        Assert.Equal(100, result.Months[4].RedirectedToInvestments);
        Assert.Equal(1070, result.Months[^1].Nominal.Investments);
    }

    [Fact]
    public void IndependentLoansReleaseTheirOwnPaymentsOnDifferentMonths()
    {
        var result = Calculate(loans: [Loan(100, 100), Loan(250, 100)]);
        Assert.Equal(0, result.Months[1].RedirectedToInvestments);
        Assert.Equal(100, result.Months[2].RedirectedToInvestments);
        Assert.Equal(150, result.Months[3].RedirectedToInvestments);
        Assert.Equal(200, result.Months[4].RedirectedToInvestments);
        Assert.Equal(result.Months[1].Date, result.Months[4].FinancedAssets[0].PayoffDate);
        Assert.Equal(result.Months[3].Date, result.Months[4].FinancedAssets[1].PayoffDate);
    }

    [Fact]
    public void LoanInterestIsChargedBeforePaymentsAndDoesNotCountAsEquity()
    {
        var result = Calculate(loans: [Loan(balance: 1000, payment: 110, interest: 12)]);
        var first = result.Months[1];
        Assert.Equal(10, first.FinancedAssets[0].InterestCharged);
        Assert.Equal(900, first.Nominal.LoanBalance);
        Assert.Equal(100, first.Nominal.Equity);
        Assert.Equal(100, first.Nominal.NetPosition);
    }

    [Fact]
    public void FinalPaymentIncludesInterestBeforeReleasingItsRemainder()
    {
        var result = Calculate(loans: [Loan(balance: 100, payment: 200, interest: 12)]);
        var month = result.Months[1];
        Assert.Equal(1, month.FinancedAssets[0].InterestCharged);
        Assert.Equal(101, month.FinancedAssets[0].PaymentApplied);
        Assert.Equal(99, month.RedirectedToInvestments);
        Assert.Equal(0, month.Nominal.LoanBalance);
        Assert.Equal(1099, month.Nominal.NetPosition);
    }

    [Theory]
    [InlineData(5, 1005)]
    [InlineData(10, 1000)]
    [InlineData(0, 1010)]
    public void InsufficientPaymentsWarnOnceAndDoNotCreateAnInventedPayoff(int payment, int balanceAfterMonthOne)
    {
        var result = Calculate(loans: [Loan(balance: 1000, payment: payment, interest: 12)]);
        Assert.Equal(balanceAfterMonthOne, result.Months[1].Nominal.LoanBalance);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ProjectionWarningKind.PaymentDoesNotReduceLoanBalance, warning.Kind);
        Assert.Equal(result.Months[1].Date, warning.FirstOccurrence);
        Assert.Null(result.Months[^1].FinancedAssets[0].PayoffDate);
        Assert.All(result.Months, month => Assert.Equal(0, month.RedirectedToInvestments));
    }

    [Fact]
    public void ConfirmedPaidOffLoanReleasesPaymentFromFirstFutureMonth()
    {
        var result = Calculate(loans: [Loan(balance: 0)]);
        Assert.Equal(Start, result.Months[0].FinancedAssets[0].PayoffDate);
        Assert.Equal(0, result.Months[0].RedirectedToInvestments);
        Assert.Equal(100, result.Months[1].RedirectedToInvestments);
        Assert.Equal(1200, result.Months[^1].Nominal.Investments);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void NativeBalancesAndRedirectedPaymentsUseFixedQuoteExactlyOnce()
    {
        var result = Calculate(loans: [Loan(balance: 1000, payment: 2000, value: 10000, currency: "ZAR", fx: .05m)]);
        Assert.Equal(1000, result.Months[0].FinancedAssets[0].LoanBalance);
        Assert.Equal(50, result.Months[0].Nominal.LoanBalance);
        Assert.Equal(500, result.Months[0].Nominal.FinancedAssetValue);
        Assert.Equal(1000, result.Months[1].FinancedAssets[0].RedirectedToInvestments);
        Assert.Equal(50, result.Months[1].RedirectedToInvestments);
        Assert.Equal(50, result.Months[1].Nominal.Investments);
        Assert.Equal(100, result.Months[2].RedirectedToInvestments);
    }

    [Theory]
    [InlineData(10, 1100, 1000)]
    [InlineData(-10, 900, 1000)]
    public void InflationChangesPurchasingPowerWithoutChangingNominalPayments(int inflation, int initial, int adjusted)
    {
        var result = Calculate(Assumptions with { AnnualInflationPercent = inflation }, initial);
        Assert.Equal(initial, result.Months[^1].Nominal.Investments);
        Near(adjusted, result.Months[^1].InflationAdjusted.Investments);
        var withToggle = Calculate(Assumptions with { AnnualInflationPercent = inflation, ShowInflationAdjustedValues = true }, initial);
        Assert.Equal(result.Months[^1].Nominal, withToggle.Months[^1].Nominal);
        Assert.Equal(result.Months[^1].InflationAdjusted, withToggle.Months[^1].InflationAdjusted);
    }

    [Fact]
    public void RepeatedCalculationsDoNotMutateStartingInputs()
    {
        var loan = Loan();
        var first = Calculate(loans: [loan]);
        var second = Calculate(loans: [loan]);
        Assert.Equal(250, loan.LoanBalance);
        Assert.Equal(first.Months[^1].Nominal, second.Months[^1].Nominal);
    }

    [Fact]
    public void NearMinusOneHundredPercentGrowthRetainsPositiveRepresentableBalances()
    {
        const decimal rate = -99.999999999999999999m;
        const decimal initial = 100_000_000_000_000_000_000m;
        var result = Calculate(Assumptions with { Currencies = [Assumptions.Currencies[0] with { AnnualInvestmentReturnPercent = rate, AnnualSavingsInterestPercent = rate }] }, initial, initial, Loan(balance: 0, payment: 0, growth: rate, value: initial));
        Assert.All(result.Months, month =>
        {
            Assert.True(month.Nominal.Investments > 0);
            Assert.True(month.Nominal.Savings > 0);
            Assert.True(month.Nominal.FinancedAssetValue > 0);
        });
        Near(1, result.Months[^1].Nominal.Investments);
        Near(1, result.Months[^1].Nominal.Savings);
        Near(1, result.Months[^1].Nominal.FinancedAssetValue);
    }

    [Fact]
    public void NearMinusOneHundredPercentInflationRetainsItsPositiveFactor()
    {
        var result = Calculate(Assumptions with { AnnualInflationPercent = -99.999999999999999999m },
            investments: .00000000000000000001m);
        Near(1, result.Months[^1].InflationAdjusted.Investments);
    }

    [Theory]
    [InlineData("Investment")]
    [InlineData("Savings")]
    [InlineData("Asset")]
    [InlineData("Inflation")]
    public void UnsupportedCompoundingUnderflowIsReportedInsteadOfSilentlyErasingBalances(string component)
    {
        const decimal rate = -99.999999999999999999m;
        var assumptions = Assumptions with { DurationYears = 2, AnnualInflationPercent = component == "Inflation" ? rate : 0, Currencies = [Assumptions.Currencies[0] with { AnnualInvestmentReturnPercent = component == "Investment" ? rate : 0, AnnualSavingsInterestPercent = component == "Savings" ? rate : 0 }] };
        var failure = Assert.Throws<InvalidValueException>(() => Calculate(assumptions,
            investments: component == "Investment" ? 1 : 0,
            savings: component == "Savings" ? 1 : 0,
            loans: component == "Asset" ? [Loan(balance: 0, payment: 0, growth: rate, value: 1)] : []));
        Assert.Contains("numeric limits", failure.Message);
    }

    [Fact]
    public void DebtExceedingAssetValueProducesNegativeEquityAndNetPosition()
    {
        var result = Calculate(investments: 100, savings: 50,
            loans: [Loan(balance: 2000, payment: 100, value: 1000)]);
        Assert.Equal(-1000, result.Months[0].Nominal.Equity);
        Assert.Equal(-850, result.Months[0].Nominal.NetPosition);
        Assert.Equal(-900, result.Months[1].Nominal.Equity);
        Assert.Equal(-750, result.Months[1].Nominal.NetPosition);
    }

    [Fact]
    public void ZeroInterestZeroPaymentLeavesDebtUnchangedAndWarnsOnce()
    {
        var loan = Loan(balance: 1000, payment: 0, interest: 0);
        var result = Calculate(loans: [loan]);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(loan.Assumptions.FinancedAssetId, warning.FinancedAssetId);
        Assert.Equal(ProjectionWarningKind.PaymentDoesNotReduceLoanBalance, warning.Kind);
        Assert.Equal(result.Months[1].Date, warning.FirstOccurrence);
        Assert.All(result.Months, month =>
        {
            Assert.Equal(1000, month.Nominal.LoanBalance);
            Assert.Equal(0, month.RedirectedToInvestments);
            var asset = Assert.Single(month.FinancedAssets);
            Assert.Equal(0, asset.InterestCharged);
            Assert.Equal(0, asset.PaymentApplied);
            Assert.Null(asset.PayoffDate);
        });
    }

    [Theory]
    [InlineData("Valuation")]
    [InlineData("Debt")]
    [InlineData("RedirectedPayment")]
    public void CurrencyConversionRejectsBalancesAndCashFlowsThatRoundToZero(string component)
    {
        const decimal tiny = .00000000001m;
        var loan = Loan(balance: component == "Debt" ? tiny : 0,
            value: component == "Valuation" ? tiny : 1,
            payment: component == "RedirectedPayment" ? tiny : 0,
            currency: "ZAR", fx: .000000000000000001m);
        var failure = Assert.Throws<InvalidValueException>(() => Calculate(loans: [loan]));
        Assert.Contains("numeric limits", failure.Message);
    }

    [Fact]
    public void RejectsMissingInvalidAndDuplicateLoanInputs()
    {
        var loan = Loan();
        Assert.Throws<InvalidValueException>(() => Calculate(loans: [null!]));
        Assert.Throws<InvalidValueException>(() => Calculate(loans: [loan with { Assumptions = null! }]));
        Assert.Throws<InvalidValueException>(() => Calculate(loans: [loan, loan]));
        Assert.Throws<InvalidValueException>(() => Calculate(loans: [loan with { LoanBalance = -1 }]));
        Assert.Throws<InvalidValueException>(() => Calculate(loans: [loan with { ConversionRate = 0 }]));
        Assert.Throws<InvalidValueException>(() => Calculate(loans: [loan with { ConversionRate = 2 }]));
        Assert.Throws<InvalidValueException>(() => Calculate(loans: [loan with { Assumptions = loan.Assumptions with { FinancedAssetId = Guid.Empty } }]));
    }

    [Fact]
    public void RejectsInvalidAssumptionsDatesAndUnrepresentableResults()
    {
        Assert.Throws<InvalidValueException>(() => Calculate(Assumptions with { Currencies = [Assumptions.Currencies[0] with { AnnualInvestmentReturnPercent = -100 }] }));
        Assert.Throws<InvalidValueException>(() => Calculate(Assumptions with { DurationYears = 0 }));
        Assert.Throws<InvalidValueException>(() => Calculate(investments: -1));
        Assert.Throws<InvalidValueException>(() => ProjectionCalculator.Calculate(Assumptions, new(new(9999, 1, 1), [new("GBP", 0, 0, 1)], [])));
        Assert.Throws<InvalidValueException>(() => Calculate(Assumptions with { DurationYears = 100, Currencies = [Assumptions.Currencies[0] with { AnnualInvestmentReturnPercent = 100 }] }, 1_000_000));
    }
}
