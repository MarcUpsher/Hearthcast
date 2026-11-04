using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries.Models.Merged;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Summaries;

public sealed class MergedSnapshotSummaryService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<MergedSnapshotSummary> GetAsync(Guid householdId, Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var snapshot = await new SnapshotService(contextFactory).GetAsync(householdId, snapshotId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return MergedSnapshotSummaryCalculator.Calculate(snapshot);
        }
        catch (OverflowException)
        {
            throw new InvalidValueException("The converted summary exceeds the supported numeric range.");
        }
    }
}
