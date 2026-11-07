namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Combined balances in the projection currency, covering only the supplied model inputs.</summary>
public sealed record ProjectionBalances(decimal Investments, decimal Savings, decimal FinancedAssetValue, decimal LoanBalance)
{
    public decimal Equity => FinancedAssetValue - LoanBalance;
    public decimal NetPosition => Investments + Savings + Equity;
}
