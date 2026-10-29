namespace Hearthcast.Data.Snapshots;

public readonly record struct ItemSnapshotValue(Guid TrackedItemId, decimal? Amount);
