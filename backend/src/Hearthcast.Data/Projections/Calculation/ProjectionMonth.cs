namespace Hearthcast.Data.Projections.Calculation;

public sealed record ProjectionMonth(int Month, DateOnly Date, ProjectionBalances Nominal,
    ProjectionBalances InflationAdjusted, decimal RedirectedToInvestments,
    IReadOnlyList<ProjectionLoanMonth> FinancedAssets, IReadOnlyList<ProjectionCurrencyMonth> Currencies,
    ProjectionExpenseTargetMonth? ExpenseTarget = null);
