using Hearthcast.Data.Summaries.Models;

namespace Hearthcast.Data.Summaries;

internal static class SnapshotSummaryAggregation
{
    public static SnapshotAmountSubtotal Subtotal(IEnumerable<(decimal? Amount, bool Carried)> entries)
    {
        var values = entries.ToArray();
        return new(values.Sum(value => value.Amount ?? 0m), values.Count(value => !value.Amount.HasValue),
            values.Count(value => value.Amount.HasValue && value.Carried));
    }

    public static SnapshotSummaryTotals Combine(IEnumerable<SnapshotSummaryTotals> totals)
    {
        var values = totals.ToArray();
        return new(Sum(values.Select(value => value.OrdinaryAssets)), Sum(values.Select(value => value.OrdinaryDebts)),
            Sum(values.Select(value => value.FinancedAssetEquity)));
    }

    private static SnapshotAmountSubtotal Sum(IEnumerable<SnapshotAmountSubtotal> subtotals)
    {
        var values = subtotals.ToArray();
        return new(values.Sum(value => value.KnownSubtotal), values.Sum(value => value.MissingEntryCount),
            values.Sum(value => value.CarriedForwardEntryCount));
    }

}
