namespace Hearthcast.Data.Snapshots;

public sealed record SnapshotActivity(SnapshotInventoryEntry? LatestSnapshot,
    IReadOnlyList<TrackedItemActivity> Items, IReadOnlyList<FinancedAssetActivity> FinancedAssets);

public sealed record TrackedItemActivity(Guid TrackedItemId, DateOnly LastIncludedDate,
    DateOnly? LastConfirmedDate, DatePrecision? LastConfirmedPrecision,
    bool PresentInLatestSnapshot, SnapshotValueOrigin? LatestValueOrigin);

public sealed record FinancedAssetActivity(Guid FinancedAssetId, DateOnly LastIncludedDate,
    bool PresentInLatestSnapshot, bool HasCarriedValuesInLatestSnapshot,
    IReadOnlyList<FinancedBalanceActivity> Balances);

public sealed record FinancedBalanceActivity(FinancedAssetBalanceKind Balance,
    DateOnly? LastConfirmedDate, DatePrecision? LastConfirmedPrecision);
