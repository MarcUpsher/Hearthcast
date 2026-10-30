using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models.Household;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Summaries;

public sealed class HouseholdSummaryService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<SnapshotHouseholdSummary> GetAsync(
        Guid householdId, Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var snapshot = await new SnapshotService(contextFactory).GetAsync(householdId, snapshotId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return HouseholdSummaryCalculator.Calculate(snapshot);
    }
}
