namespace Hearthcast.Data.Spending;

public sealed class SpendingRecordConflictException(Exception? innerException = null)
    : InvalidOperationException("The spending record has changed. Reload it before correcting it.", innerException);
