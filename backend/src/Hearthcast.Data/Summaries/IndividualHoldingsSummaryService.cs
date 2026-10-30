using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models.IndividualHoldings;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Summaries;

public sealed class IndividualHoldingsSummaryService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<SnapshotIndividualHoldingsSummary> GetAsync(
        Guid householdId, Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var snapshot = await new SnapshotService(contextFactory).GetAsync(householdId, snapshotId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return IndividualHoldingsSummaryCalculator.Calculate(SnapshotReportProjector.Project(snapshot));
    }
}
