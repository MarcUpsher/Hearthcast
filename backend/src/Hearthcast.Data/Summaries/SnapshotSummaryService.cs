using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Summaries;

public sealed class SnapshotSummaryService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<SnapshotSummary> GetAsync(
        Guid householdId, Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var snapshot = await new SnapshotService(contextFactory).GetAsync(householdId, snapshotId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return SnapshotSummaryCalculator.Calculate(snapshot);
    }
}
