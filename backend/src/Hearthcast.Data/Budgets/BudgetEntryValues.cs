namespace Hearthcast.Data.Budgets;

public sealed record BudgetEntryValues(Guid BudgetItemId, BudgetEntryValueMode Mode,
    decimal? PlannedAmount = null, DateOnly? ConfirmedOn = null, BudgetEntrySource? Source = null,
    bool RefreshFromSetup = false, BudgetEntryMetadataValues? Metadata = null);
