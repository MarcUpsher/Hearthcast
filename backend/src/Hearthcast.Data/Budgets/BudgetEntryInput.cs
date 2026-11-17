namespace Hearthcast.Data.Budgets;

internal sealed record BudgetEntryInput(BudgetEntryMetadata Metadata, decimal? PlannedAmount = null,
    DateOnly? ConfirmedOn = null, BudgetSnapshot? CarrySourceSnapshot = null, Guid? CarrySourceEntryId = null)
{
    internal static BudgetEntryInput Carry(BudgetSnapshot source, BudgetEntry entry) =>
        new(BudgetEntryMetadata.FromEntry(entry), CarrySourceSnapshot: source, CarrySourceEntryId: entry.Id);
}
