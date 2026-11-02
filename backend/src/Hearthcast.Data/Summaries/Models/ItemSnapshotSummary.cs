namespace Hearthcast.Data.Summaries.Models;

public sealed record ItemSnapshotSummary(Guid ItemSnapshotId, Guid TrackedItemId, string ItemName,
    string CountryCode, Guid HolderSnapshotPersonId, bool IncludeInHousehold,
    SnapshotBalanceSummary Balance, IReadOnlyList<SnapshotPersonShare> HouseholdAllocations,
    string? InstitutionName = null, string? FundName = null);
