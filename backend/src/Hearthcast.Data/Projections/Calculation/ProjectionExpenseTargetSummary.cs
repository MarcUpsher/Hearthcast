namespace Hearthcast.Data.Projections.Calculation;

public sealed record ProjectionExpenseTargetSummary(string ExpenseCurrencyCode, decimal AnnualExpenses,
    decimal AnnualWithdrawalRatePercent, bool IncludeSavings, decimal RequiredCapitalInExpenseCurrency,
    DateOnly? FirstReachedDate, bool AlreadyReachedAtStart);
