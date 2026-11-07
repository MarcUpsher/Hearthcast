using Hearthcast.Data.Projections.Calculation;

namespace Hearthcast.Data.Projections.Preparation;

public sealed record ProjectionPreparation(Guid SnapshotId, Guid SnapshotRevision,
    ProjectionPresetSettings Assumptions, ProjectionStartingPosition? StartingPosition,
    IReadOnlyList<ProjectionPreparationIssue> Issues, IReadOnlyList<ProjectionCoverageEntry> Coverage)
{
    public bool CanCalculate => StartingPosition is not null && !Issues.Any(i => i.Severity == ProjectionIssueSeverity.Error);
}
