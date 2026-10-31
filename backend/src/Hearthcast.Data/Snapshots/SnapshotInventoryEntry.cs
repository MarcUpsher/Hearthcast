namespace Hearthcast.Data.Snapshots;

public sealed record SnapshotInventoryEntry(Guid Id, DateOnly SnapshotDate, DatePrecision DatePrecision, Guid Revision);
