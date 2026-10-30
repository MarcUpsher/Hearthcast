namespace Hearthcast.Data.Snapshots;

public sealed class SnapshotConflictException(Exception? innerException = null)
    : InvalidOperationException("The snapshot has changed. Reload it before applying corrections.", innerException);
