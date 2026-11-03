namespace Hearthcast.Data.Snapshots;

public sealed class SnapshotExchangeRate
{
    private SnapshotExchangeRate() { }

    internal SnapshotExchangeRate(Guid snapshotId, string baseCurrency, string quoteCurrency,
        decimal rate, DateOnly effectiveDate, string source)
    {
        SnapshotId = snapshotId;
        BaseCurrency = baseCurrency;
        QuoteCurrency = quoteCurrency;
        Rate = rate;
        EffectiveDate = effectiveDate;
        Source = source;
        RetrievedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid SnapshotId { get; private set; }
    public string BaseCurrency { get; private set; } = "";
    public string QuoteCurrency { get; private set; } = "";
    public decimal Rate { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public string Source { get; private set; } = "";
    public DateTimeOffset RetrievedAtUtc { get; private set; }
}
