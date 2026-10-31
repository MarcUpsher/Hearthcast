using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Classification;

namespace Hearthcast.Api.Snapshots.Contracts;

public sealed record ItemSnapshotResponse(
    Guid Id,
    Guid HouseholdId,
    Guid SnapshotId,
    Guid TrackedItemId,
    Guid HolderSnapshotPersonId,
    string ItemName,
    Guid CategoryId,
    string CategoryName,
    FinancialType FinancialType,
    Guid GroupId,
    string GroupName,
    string CurrencyCode,
    string CountryCode,
    bool IncludeInHousehold,
    decimal? Amount,
    SnapshotValueOrigin ValueOrigin,
    DateOnly? LastConfirmedDate,
    DatePrecision? LastConfirmedPrecision,
    Guid? CarriedFromItemSnapshotId,
    IReadOnlyList<SnapshotShareResponse> HouseholdAllocations)
{
    internal static ItemSnapshotResponse From(ItemSnapshot value) => new(
        value.Id,
        value.HouseholdId,
        value.SnapshotId,
        value.TrackedItemId,
        value.HolderSnapshotPersonId,
        value.ItemName,
        value.CategoryId,
        value.CategoryName,
        value.FinancialType,
        value.GroupId,
        value.GroupName,
        value.CurrencyCode,
        value.CountryCode,
        value.IncludeInHousehold,
        value.Amount,
        value.ValueOrigin,
        value.LastConfirmedDate,
        value.LastConfirmedPrecision,
        value.CarriedFromItemSnapshotId,
        value.HouseholdAllocations.OrderBy(row => row.SnapshotPersonId).Select(row => new SnapshotShareResponse(row.SnapshotPersonId, row.ShareBasisPoints)).ToArray());
}
