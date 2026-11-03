using System.Text.Json;
using Npgsql;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hearthcast.Data.ExchangeRates;

public sealed class SnapshotExchangeRateService(IDbContextFactory<HearthcastDbContext> contextFactory,
    SnapshotService snapshots, IExchangeRateProvider provider, ILogger<SnapshotExchangeRateService> logger)
{
    public async Task<Snapshot> CaptureMissingAsync(Guid householdId, Guid snapshotId, Guid expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var saved = await snapshots.GetAsync(householdId, snapshotId, cancellationToken);
        if (saved.Revision != expectedRevision) throw new SnapshotConflictException();
        if (saved.ExchangeRateLookupDate is null || saved.ExchangeRateBaseCurrency is null
            || saved.MissingExchangeRateCurrencies.Count == 0) return saved;

        IReadOnlyList<ExchangeRateQuote> quotes;
        try
        {
            // The financial save has committed. Never hold a database transaction during HTTP requests.
            quotes = await provider.GetAsync(saved.ExchangeRateBaseCurrency, saved.MissingExchangeRateCurrencies,
                saved.ExchangeRateLookupDate.Value, cancellationToken);
            saved.ValidateExchangeRates(quotes);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Exchange rates are unavailable for snapshot {SnapshotId}; financial values remain saved.", snapshotId);
            return await snapshots.GetAsync(householdId, snapshotId, cancellationToken);
        }

        try
        {
            await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
            {
                var current = await context.Snapshots.Include(s => s.ExchangeRates)
                    .SingleAsync(s => s.Id == snapshotId && s.HouseholdId == householdId, token);
                // Another edit or rate capture won the race. Its result takes precedence.
                if (current.Revision != expectedRevision) return false;
                foreach (var quote in quotes)
                    context.Set<SnapshotExchangeRate>().Add(new SnapshotExchangeRate(snapshotId,
                        quote.BaseCurrency, quote.QuoteCurrency, quote.Rate, quote.EffectiveDate, provider.Source));
                context.Entry(current).Property(s => s.Revision).CurrentValue = Guid.NewGuid();
                context.Entry(current).Property(s => s.UpdatedAtUtc).CurrentValue = DateTimeOffset.UtcNow;
                return true;
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // The financial save is already committed. A failed/ambiguous rate commit must be
            // reconciled by reading the database, rather than encouraging another financial save.
            logger.LogWarning(exception, "Could not confirm exchange-rate storage for snapshot {SnapshotId}; reloading saved state.", snapshotId);
        }

        return await snapshots.GetAsync(householdId, snapshotId, cancellationToken);
    }
}
