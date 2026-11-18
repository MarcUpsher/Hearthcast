namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingReconciliationCountsResponse(int NotChecked, int Reconciled, int Unreconciled);
