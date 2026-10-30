namespace Hearthcast.Data.Snapshots;

public readonly record struct FinancedAssetSnapshotValue(
    Guid FinancedAssetId, FinancedAssetBalanceKind Balance, decimal? Amount);
