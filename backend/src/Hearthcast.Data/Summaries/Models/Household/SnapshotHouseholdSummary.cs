using Hearthcast.Data.Snapshots;

namespace Hearthcast.Data.Summaries.Models.Household;

public sealed record SnapshotHouseholdSummary(Guid SnapshotId, Guid HouseholdId, string HouseholdName,
    DateOnly SnapshotDate, DatePrecision DatePrecision, Guid Revision, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, IReadOnlyList<HouseholdCurrencySummary> Currencies,
    IReadOnlyList<HouseholdPersonSummary> People);
