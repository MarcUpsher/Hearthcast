namespace Hearthcast.Data.Projections.Calculation;

internal sealed class ProjectionGrowthAccumulator
{
    private readonly Totals _nominal = new();
    private readonly Totals _adjusted = new();

    public void Add(ProjectionBalances previous, ProjectionBalances current,
        decimal investmentContributions, decimal savingsContributions, decimal reinvestedLoanPayments,
        decimal previousInflation, decimal inflation)
    {
        _nominal.Add(previous, current, investmentContributions, savingsContributions, reinvestedLoanPayments, 1, 0);
        // Contributions are valued when made. The separate inflation effect reconciles the
        // purchasing-power change on wealth already held at the start of this month.
        var inflationEffect = previous.NetPosition / inflation - previous.NetPosition / previousInflation;
        _adjusted.Add(previous, current, investmentContributions, savingsContributions, reinvestedLoanPayments,
            inflation, inflationEffect);
    }

    public ProjectionGrowthSummary Finish(decimal startingNetPosition, decimal finalNominalNetPosition,
        decimal finalAdjustedNetPosition) => new(
        _nominal.ToBreakdown(startingNetPosition, finalNominalNetPosition),
        _adjusted.ToBreakdown(startingNetPosition, finalAdjustedNetPosition));

    private sealed class Totals
    {
        private decimal _investmentReturns;
        private decimal _savingsInterest;
        private decimal _financedAssetValueChange;
        private decimal _investmentContributions;
        private decimal _savingsContributions;
        private decimal _loanBalanceReduction;
        private decimal _reinvestedLoanPayments;
        private decimal _inflationEffect;

        public void Add(ProjectionBalances previous, ProjectionBalances current,
            decimal investmentContributions, decimal savingsContributions, decimal reinvestedLoanPayments,
            decimal priceFactor, decimal inflationEffect)
        {
            _investmentReturns += (current.Investments - previous.Investments - investmentContributions - reinvestedLoanPayments) / priceFactor;
            _savingsInterest += (current.Savings - previous.Savings - savingsContributions) / priceFactor;
            _financedAssetValueChange += (current.FinancedAssetValue - previous.FinancedAssetValue) / priceFactor;
            _investmentContributions += investmentContributions / priceFactor;
            _savingsContributions += savingsContributions / priceFactor;
            _loanBalanceReduction += (previous.LoanBalance - current.LoanBalance) / priceFactor;
            _reinvestedLoanPayments += reinvestedLoanPayments / priceFactor;
            _inflationEffect += inflationEffect;
        }

        public ProjectionGrowthBreakdown ToBreakdown(decimal starting, decimal final) => new(
            starting, _investmentReturns, _savingsInterest, _financedAssetValueChange,
            _investmentContributions, _savingsContributions, _loanBalanceReduction,
            _reinvestedLoanPayments, _inflationEffect, final);
    }
}
