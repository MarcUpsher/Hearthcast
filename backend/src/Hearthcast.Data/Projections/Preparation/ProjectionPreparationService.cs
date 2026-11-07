using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Projections.Preparation;

public sealed class ProjectionPreparationService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<ProjectionPreparation> PrepareAsync(Guid householdId, Guid snapshotId,
        ProjectionPresetSettings assumptions, IReadOnlyCollection<ProjectionGroupSelection> groups,
        IReadOnlyCollection<ProjectionFinancedAssetSettings> assets, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assumptions);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(assets);
        ProjectionRules.ValidateAssumptions(assumptions);
        var selections = groups.ToArray();
        var loans = assets.ToArray();
        ProjectionRules.ValidateSelections(selections, loans);
        var currency = RequiredAlphabeticCode.Normalize(assumptions.CurrencyCode, 3);
        var snapshot = await new SnapshotService(contextFactory).GetAsync(householdId, snapshotId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return ProjectionSnapshotAdapter.Prepare(snapshot, assumptions with { CurrencyCode = currency }, selections, loans);
        }
        catch (OverflowException)
        {
            throw new InvalidValueException("The starting projection balances exceed supported numeric limits.");
        }
    }

    public async Task<ProjectionPreparation> PrepareSavedPresetAsync(Guid householdId, Guid snapshotId, Guid presetId,
        CancellationToken cancellationToken = default)
    {
        var p = await new ProjectionPresetService(contextFactory).GetAsync(householdId, presetId, cancellationToken);
        return await PrepareAsync(householdId, snapshotId,
            new(p.Name, p.CurrencyCode, p.DurationYears, p.MonthlyInvestmentContribution,
                p.AnnualInvestmentReturnPercent, p.MonthlySavingsContribution, p.AnnualSavingsInterestPercent,
                p.AnnualInflationPercent, p.ShowInflationAdjustedValues),
            p.Groups.Select(g => new ProjectionGroupSelection(g.GroupId, g.Bucket)).ToArray(),
            p.FinancedAssets.Select(a => new ProjectionFinancedAssetSettings(a.FinancedAssetId,
                a.MonthlyLoanPaymentInAssetCurrency, a.AnnualLoanInterestPercent, a.AnnualAssetAppreciationPercent)).ToArray(),
            cancellationToken);
    }
}
