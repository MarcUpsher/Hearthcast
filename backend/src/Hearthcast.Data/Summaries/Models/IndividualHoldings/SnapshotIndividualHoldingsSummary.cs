using Hearthcast.Data.Snapshots;

namespace Hearthcast.Data.Summaries.Models.IndividualHoldings;

public sealed record SnapshotIndividualHoldingsSummary(
    Guid SnapshotId, Guid HouseholdId, string HouseholdName, DateOnly SnapshotDate,
    DatePrecision DatePrecision, Guid Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<PersonHoldingsSummary> People);
