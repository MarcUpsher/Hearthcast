using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetCurrentOutgoingsResponse(DateOnly AsOf, Guid? SnapshotId, DateOnly? EffectiveFrom,
    DateOnly? EffectiveUntilExclusive, bool? IsComplete, IReadOnlyList<BudgetCurrentOutgoingResponse> Items)
{
    internal static BudgetCurrentOutgoingsResponse From(BudgetCurrentOutgoings read) => new(read.AsOf, read.SnapshotId,
        read.EffectiveFrom, read.EffectiveUntilExclusive, read.IsComplete, read.Items.Select(i =>
            new BudgetCurrentOutgoingResponse(BudgetItemResponse.From(i.Item), i.Entry is null ? null : BudgetEntryResponse.From(i.Entry))).ToArray());
}
