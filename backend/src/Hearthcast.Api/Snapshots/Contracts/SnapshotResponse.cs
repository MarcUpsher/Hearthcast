using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Classification;

namespace Hearthcast.Api.Snapshots.Contracts;

public sealed record SnapshotResponse(
    Guid Id,
    Guid HouseholdId,
    string HouseholdName,
    DateOnly SnapshotDate,
    DatePrecision DatePrecision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid Revision,
    IReadOnlyList<SnapshotPersonResponse> People,
    IReadOnlyList<ItemSnapshotResponse> ItemSnapshots,
    IReadOnlyList<FinancedAssetSnapshotResponse> FinancedAssetSnapshots)
{
    public DateOnly? ExchangeRateLookupDate { get; init; }
    public string? ExchangeRateBaseCurrency { get; init; }
    public string ExchangeRateStatus { get; init; } = "NotCaptured";
    public IReadOnlyList<SnapshotExchangeRateResponse> ExchangeRates { get; init; } = [];

    internal static SnapshotResponse From(Snapshot value) => new(
        value.Id,
        value.HouseholdId,
        value.HouseholdName,
        value.SnapshotDate,
        value.DatePrecision,
        DatabaseTimestamp(value.CreatedAtUtc),
        DatabaseTimestamp(value.UpdatedAtUtc),
        value.Revision,
        value.People.OrderBy(row => row.Id).Select(SnapshotPersonResponse.From).ToArray(),
        value.ItemSnapshots.OrderBy(row => row.Id).Select(ItemSnapshotResponse.From).ToArray(),
        value.FinancedAssetSnapshots.OrderBy(row => row.Id).Select(FinancedAssetSnapshotResponse.From).ToArray())
    {
        ExchangeRateLookupDate = value.ExchangeRateLookupDate,
        ExchangeRateBaseCurrency = value.ExchangeRateBaseCurrency,
        ExchangeRateStatus = value.ExchangeRateStatus,
        ExchangeRates = value.ExchangeRates.OrderBy(rate => rate.QuoteCurrency).Select(rate =>
            new SnapshotExchangeRateResponse(rate.BaseCurrency, rate.QuoteCurrency, rate.Rate,
                rate.EffectiveDate, rate.Source, DatabaseTimestamp(rate.RetrievedAtUtc))).ToArray()
    };

    // PostgreSQL stores microseconds; keep POST and subsequent GET timestamps identical.
    private static DateTimeOffset DatabaseTimestamp(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
}

public sealed record SnapshotExchangeRateResponse(string BaseCurrency, string QuoteCurrency, decimal Rate,
    DateOnly EffectiveDate, string Source, DateTimeOffset RetrievedAtUtc);
