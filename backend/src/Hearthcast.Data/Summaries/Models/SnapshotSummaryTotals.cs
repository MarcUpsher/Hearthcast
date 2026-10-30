namespace Hearthcast.Data.Summaries.Models;

/// <summary>Totals within one report scope and currency. Debt is a positive subtotal subtracted from the net position.</summary>
public sealed record SnapshotSummaryTotals
{
    public SnapshotSummaryTotals(SnapshotAmountSubtotal ordinaryAssets,
        SnapshotAmountSubtotal ordinaryDebts, SnapshotAmountSubtotal financedAssetEquity)
    {
        OrdinaryAssets = ordinaryAssets;
        OrdinaryDebts = ordinaryDebts;
        FinancedAssetEquity = financedAssetEquity;
        // Validate while constructing the response, before it reaches a caller or serializer.
        KnownNetPosition = ordinaryAssets.KnownSubtotal - ordinaryDebts.KnownSubtotal + financedAssetEquity.KnownSubtotal;
    }

    public SnapshotAmountSubtotal OrdinaryAssets { get; }
    public SnapshotAmountSubtotal OrdinaryDebts { get; }
    public SnapshotAmountSubtotal FinancedAssetEquity { get; }
    public decimal KnownNetPosition { get; }
    public bool IsComplete => OrdinaryAssets.IsComplete && OrdinaryDebts.IsComplete && FinancedAssetEquity.IsComplete;
}
