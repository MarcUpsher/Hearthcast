using Hearthcast.Data.Snapshots;

namespace Hearthcast.Data.Summaries.Models;

/// <param name="CarriedFromEntryId">Source item or financed-asset entry ID, not the parent snapshot ID.</param>
public sealed record SnapshotBalanceSummary(decimal? Amount, SnapshotValueOrigin ValueOrigin,
    DateOnly? LastConfirmedDate, DatePrecision? LastConfirmedPrecision, Guid? CarriedFromEntryId);
