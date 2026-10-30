using Hearthcast.Data.Snapshots;

namespace Hearthcast.Data.Summaries.Models;

/// <summary>A calculated overview of everything recorded, including holdings excluded from household finances.</summary>
public sealed record SnapshotSummary(
    Guid SnapshotId, Guid HouseholdId, string HouseholdName, DateOnly SnapshotDate,
    DatePrecision DatePrecision, Guid Revision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<SnapshotPersonSummary> People, IReadOnlyList<SnapshotCurrencySummary> Currencies);
