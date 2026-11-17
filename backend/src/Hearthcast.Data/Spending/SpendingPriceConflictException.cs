namespace Hearthcast.Data.Spending;

public sealed class SpendingPriceConflictException(Exception? innerException = null)
    : InvalidOperationException("The usual price has changed. Reload it before updating it.", innerException);
