using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hearthcast.Data.ExchangeRates;

public sealed record HistoricalExchangeRateBackfillEntry(Guid SnapshotId, DateOnly SnapshotDate,
    DateOnly? LookupDate, string Status);
public sealed record HistoricalExchangeRateBackfillReport(int Total, int Initialized, int Ready,
    int NotRequired, int Remaining, IReadOnlyList<HistoricalExchangeRateBackfillEntry> Entries);

public sealed class HistoricalExchangeRateBackfillService(IDbContextFactory<HearthcastDbContext> contextFactory,
    SnapshotService snapshots, SnapshotExchangeRateService rates, ILogger<HistoricalExchangeRateBackfillService> logger)
{
    public async Task<HistoricalExchangeRateBackfillReport> RunAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var inventory = await context.Snapshots.AsNoTracking().OrderBy(s => s.SnapshotDate).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.HouseholdId, s.SnapshotDate }).ToArrayAsync(cancellationToken);
        var entries = new List<HistoricalExchangeRateBackfillEntry>();
        var initialized = 0;
        foreach (var entry in inventory)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var saved = await snapshots.GetAsync(entry.HouseholdId, entry.Id, cancellationToken);
                if (saved.ExchangeRateLookupDate is null)
                {
                    saved = await snapshots.InitializeHistoricalExchangeRatesAsync(entry.HouseholdId, entry.Id,
                        saved.Revision, cancellationToken);
                    initialized++;
                }
                // Complete snapshots do not make provider requests or change revisions.
                if (saved.ExchangeRateStatus == "Pending")
                {
                    saved = await rates.CaptureMissingAsync(entry.HouseholdId, entry.Id, saved.Revision, cancellationToken);
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                }
                entries.Add(new(entry.Id, entry.SnapshotDate, saved.ExchangeRateLookupDate, saved.ExchangeRateStatus));
            }
            catch (Exception exception) when (exception is SnapshotConflictException or DbUpdateConcurrencyException)
            {
                entries.Add(new(entry.Id, entry.SnapshotDate, null, "Conflict"));
            }
            logger.LogInformation("Exchange-rate backfill {Completed}/{Total}: {Date} {Status}",
                entries.Count, inventory.Length, entry.SnapshotDate, entries[^1].Status);
        }
        return new(entries.Count, initialized, entries.Count(e => e.Status == "Ready"),
            entries.Count(e => e.Status == "NotRequired"), entries.Count(e => e.Status is not ("Ready" or "NotRequired")), entries);
    }
}
