using System.Data;
using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.HistoryImport;

internal sealed record ImportResult(Guid HouseholdId, int SnapshotCount, int ItemEntryCount, int AssetEntryCount,
    int CurrencyMonthChecks, DateOnly FirstDate, DateOnly LastDate);

internal static class HistoryLoader
{
    public static async Task<ImportResult> ImportAsync(HearthcastDbContext context, HistoryFile file,
        ExpectedTotals[] expectedTotals, CancellationToken token = default)
    {
        if ((await context.Database.GetPendingMigrationsAsync(token)).Any())
            throw new InvalidOperationException("The database has pending migrations. Apply the existing Hearthcast migrations before importing; nothing was changed.");
        var plan = PreparedHistory.Create(file);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        // Block competing setup writes while checking emptiness and loading history.
        await context.Database.ExecuteSqlRawAsync("LOCK TABLE \"Households\" IN EXCLUSIVE MODE",token);
        if (await context.Households.AnyAsync(token))
            throw new InvalidOperationException("The database already contains a household. Import requires an empty application database; nothing was changed.");
        context.Households.Add(plan.Household);
        context.Categories.AddRange(plan.Categories.Values);
        context.ItemGroups.AddRange(plan.Groups.Values);
        context.TrackedItems.AddRange(plan.Items.Values);
        context.FinancedAssets.AddRange(plan.Assets.Values);
        context.Snapshots.AddRange(plan.Snapshots.Values);
        await context.SaveChangesAsync(token);
        // Read back uncommitted database rows and use the same calculators as the
        // summary services. Any mismatch throws before the transaction can commit.
        var checks = await HistoryVerifier.VerifyAsync(context,plan,expectedTotals,token);
        await transaction.CommitAsync(token);
        return new(plan.Household.Id,plan.Snapshots.Count,file.Tables.ItemValues.Length,
            file.Tables.AssetValues.Select(r => (r.SnapshotRef,r.AssetRef)).Distinct().Count(),checks,
            plan.Snapshots.Values.Min(s => s.SnapshotDate),plan.Snapshots.Values.Max(s => s.SnapshotDate));
    }
}
