namespace Hearthcast.Data.Spending;

public sealed record SpendingCategorySummary(Guid HouseholdId, DateOnly From, DateOnly To,
    IReadOnlyList<SpendingCategoryTotal> Categories,
    IReadOnlyDictionary<string, int> ReconciliationRecordCounts,
    IReadOnlyList<SpendingReceiptWarning> Warnings);
