namespace Hearthcast.Data.Projections.Calculation;

/// <summary>Signed changes reconciling the modelled starting and final net positions, in viewing-currency units.</summary>
public sealed record ProjectionGrowthBreakdown(
    decimal StartingNetPosition,
    decimal InvestmentReturns,
    decimal SavingsInterest,
    decimal FinancedAssetValueChange,
    decimal InvestmentContributions,
    decimal SavingsContributions,
    decimal LoanBalanceReduction,
    decimal ReinvestedLoanPayments,
    decimal InflationEffect,
    decimal FinalNetPosition);
