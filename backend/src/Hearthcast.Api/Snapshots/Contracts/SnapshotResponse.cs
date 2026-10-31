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
        value.FinancedAssetSnapshots.OrderBy(row => row.Id).Select(FinancedAssetSnapshotResponse.From).ToArray());

    // PostgreSQL stores microseconds; keep POST and subsequent GET timestamps identical.
    private static DateTimeOffset DatabaseTimestamp(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));
}
