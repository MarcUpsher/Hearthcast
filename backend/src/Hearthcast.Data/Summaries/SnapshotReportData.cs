using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;

namespace Hearthcast.Data.Summaries;

internal sealed record SnapshotReportData(Snapshot Header, IReadOnlyList<SnapshotPersonSummary> People,
    IReadOnlyList<SnapshotReportGroup> Groups);
