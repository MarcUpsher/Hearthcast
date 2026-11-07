using Hearthcast.Data.Classification;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Projections;

public sealed class ProjectionPresetService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public Task<ProjectionPreset> CreateAsync(Guid householdId, ProjectionPresetSettings settings,
        IReadOnlyCollection<ProjectionGroupSelection> groups, IReadOnlyCollection<ProjectionFinancedAssetSettings> assets,
        CancellationToken cancellationToken = default) => SaveAsync(householdId, null, settings, groups, assets, cancellationToken);

    public Task<ProjectionPreset> UpdateAsync(Guid householdId, Guid presetId, ProjectionPresetSettings settings,
        IReadOnlyCollection<ProjectionGroupSelection> groups, IReadOnlyCollection<ProjectionFinancedAssetSettings> assets,
        CancellationToken cancellationToken = default) => SaveAsync(householdId, presetId, settings, groups, assets, cancellationToken);

    private async Task<ProjectionPreset> SaveAsync(Guid householdId, Guid? presetId, ProjectionPresetSettings settings,
        IReadOnlyCollection<ProjectionGroupSelection> groups, IReadOnlyCollection<ProjectionFinancedAssetSettings> assets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(assets);
        try
        {
            return await SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
            {
                await RequireHousehold(context, householdId, token);
                var preset = presetId.HasValue ? await Find(context, householdId, presetId.Value, token) : new ProjectionPreset(householdId);
                preset.Update(settings, groups, assets);
                var ids = groups.Select(g => g.GroupId).ToArray();
                var selected = await context.ItemGroups.Include(g => g.Category)
                    .Where(g => g.HouseholdId == householdId && ids.Contains(g.Id)).ToArrayAsync(token);
                if (selected.Length != ids.Length) throw new ResourceNotFoundException("A selected group does not belong to this household.");
                if (selected.Any(g => g.Category.FinancialType != FinancialType.Asset))
                    throw new InvalidValueException("Investment and savings buckets require Asset groups.");
                var assetIds = assets.Select(a => a.FinancedAssetId).ToArray();
                if (await context.FinancedAssets.CountAsync(a => a.HouseholdId == householdId && assetIds.Contains(a.Id), token) != assetIds.Length)
                    throw new ResourceNotFoundException("A selected financed asset does not belong to this household.");
                if (await context.ProjectionPresets.AnyAsync(p => p.HouseholdId == householdId && p.Id != preset.Id && p.Name == preset.Name, token))
                    throw new BusinessRuleViolationException("A preset with this name already exists in this household.");
                if (!presetId.HasValue) context.ProjectionPresets.Add(preset);
                return preset;
            }, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_ProjectionPresets_HouseholdId_Name" })
        {
            throw new BusinessRuleViolationException("A preset with this name already exists in this household.");
        }
    }

    public async Task<IReadOnlyList<ProjectionPreset>> ListAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await RequireHousehold(context, householdId, cancellationToken);
        return await Graph(context).AsNoTracking().Where(p => p.HouseholdId == householdId)
            .OrderBy(p => p.Name).ThenBy(p => p.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<ProjectionPreset> GetAsync(Guid householdId, Guid presetId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await Find(context, householdId, presetId, cancellationToken);
    }

    public Task DeleteAsync(Guid householdId, Guid presetId, CancellationToken cancellationToken = default) =>
        SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            context.ProjectionPresets.Remove(await Find(context, householdId, presetId, token));
            return true;
        }, cancellationToken);

    private static IQueryable<ProjectionPreset> Graph(HearthcastDbContext context) =>
        context.ProjectionPresets.Include(p => p.Groups).Include(p => p.FinancedAssets).Include(p => p.Currencies).AsSingleQuery();

    private static async Task<ProjectionPreset> Find(HearthcastDbContext context, Guid householdId, Guid id, CancellationToken token) =>
        await Graph(context).SingleOrDefaultAsync(p => p.HouseholdId == householdId && p.Id == id, token)
        ?? throw new ResourceNotFoundException("The preset does not belong to this household.");

    private static async Task RequireHousehold(HearthcastDbContext context, Guid householdId, CancellationToken token)
    {
        if (!await context.Households.AnyAsync(h => h.Id == householdId, token))
            throw new ResourceNotFoundException("The household does not exist.");
    }
}
