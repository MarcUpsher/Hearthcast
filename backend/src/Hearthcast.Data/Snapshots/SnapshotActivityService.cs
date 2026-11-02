using System.Data;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Snapshots;

public sealed class SnapshotActivityService(IDbContextFactory<HearthcastDbContext> factory)
{
    public async Task<SnapshotActivity> GetAsync(Guid householdId, CancellationToken token = default)
    {
        await using var context = await factory.CreateDbContextAsync(token);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,token);
        if (!await context.Households.AnyAsync(h => h.Id==householdId,token))
            throw new ResourceNotFoundException("The household does not exist.");
        var latest = await context.Snapshots.AsNoTracking().Where(s => s.HouseholdId==householdId)
            .OrderByDescending(s => s.SnapshotDate).ThenBy(s => s.Id)
            .Select(s => new SnapshotInventoryEntry(s.Id,s.SnapshotDate,s.DatePrecision,s.Revision)).FirstOrDefaultAsync(token);
        // Read only activity scalars, not complete snapshot graphs or financial amounts.
        var items = await (from i in context.ItemSnapshots.AsNoTracking()
            join s in context.Snapshots on i.SnapshotId equals s.Id
            where i.HouseholdId==householdId
            select new { i.TrackedItemId,i.SnapshotId,s.SnapshotDate,i.LastConfirmedDate,i.LastConfirmedPrecision,i.ValueOrigin }).ToArrayAsync(token);
        var assets = await (from a in context.FinancedAssetSnapshots.AsNoTracking()
            join s in context.Snapshots on a.SnapshotId equals s.Id
            where a.HouseholdId==householdId
            select new { a.FinancedAssetId,a.SnapshotId,s.SnapshotDate,
                a.CurrentValuationLastConfirmedDate,a.CurrentValuationLastConfirmedPrecision,a.CurrentValuationValueOrigin,
                a.OutstandingLoanBalanceLastConfirmedDate,a.OutstandingLoanBalanceLastConfirmedPrecision,a.OutstandingLoanBalanceValueOrigin,
                a.AvailableToWithdrawLastConfirmedDate,a.AvailableToWithdrawLastConfirmedPrecision,a.AvailableToWithdrawValueOrigin }).ToArrayAsync(token);
        var itemActivity = items.GroupBy(i => i.TrackedItemId).OrderBy(g => g.Key).Select(g =>
        {
            var confirmed = g.Where(i => i.LastConfirmedDate.HasValue).OrderByDescending(i => i.LastConfirmedDate)
                .ThenBy(i => i.LastConfirmedPrecision).FirstOrDefault();
            var current = g.SingleOrDefault(i => i.SnapshotId==latest?.Id);
            return new TrackedItemActivity(g.Key,g.Max(i => i.SnapshotDate),confirmed?.LastConfirmedDate,
                confirmed?.LastConfirmedPrecision,current is not null,current?.ValueOrigin);
        }).ToArray();
        var assetActivity = assets.GroupBy(a => a.FinancedAssetId).OrderBy(g => g.Key).Select(g =>
        {
            var current = g.SingleOrDefault(a => a.SnapshotId==latest?.Id);
            var balances = new[] {
                Last(FinancedAssetBalanceKind.CurrentValuation,g.Select(a => (a.CurrentValuationLastConfirmedDate,a.CurrentValuationLastConfirmedPrecision))),
                Last(FinancedAssetBalanceKind.OutstandingLoanBalance,g.Select(a => (a.OutstandingLoanBalanceLastConfirmedDate,a.OutstandingLoanBalanceLastConfirmedPrecision))),
                Last(FinancedAssetBalanceKind.AvailableToWithdraw,g.Select(a => (a.AvailableToWithdrawLastConfirmedDate,a.AvailableToWithdrawLastConfirmedPrecision))) };
            return new FinancedAssetActivity(g.Key,g.Max(a => a.SnapshotDate),current is not null,
                current is not null && (current.CurrentValuationValueOrigin==SnapshotValueOrigin.CarriedForward
                    || current.OutstandingLoanBalanceValueOrigin==SnapshotValueOrigin.CarriedForward
                    || current.AvailableToWithdrawValueOrigin==SnapshotValueOrigin.CarriedForward),balances);
        }).ToArray();
        await transaction.CommitAsync(token);
        return new(latest,itemActivity,assetActivity);
    }

    private static FinancedBalanceActivity Last(FinancedAssetBalanceKind kind, IEnumerable<(DateOnly? Date,DatePrecision? Precision)> rows)
    {
        var last = rows.Where(r => r.Date.HasValue).OrderByDescending(r => r.Date).ThenBy(r => r.Precision).FirstOrDefault();
        return new(kind,last.Date,last.Precision);
    }
}
