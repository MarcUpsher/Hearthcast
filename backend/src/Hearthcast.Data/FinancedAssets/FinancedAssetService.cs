using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.FinancedAssets;

public sealed class FinancedAssetService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public Task<FinancedAsset> CreateAsync(
        Guid householdId, Guid groupId, string name, string currencyCode, string countryCode,
        DateOnly? purchaseDate, decimal? purchasePrice, decimal originalLoanAmount, bool hasWithdrawalFacility,
        IReadOnlyCollection<FinancedAssetShare> owners, IReadOnlyCollection<Guid> loanHolderPersonIds,
        bool includeInHousehold, IReadOnlyCollection<FinancedAssetShare> householdAllocations,
        CancellationToken cancellationToken = default)
    {
        return SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var group = await context.ItemGroups.Include(group => group.Category)
                .SingleOrDefaultAsync(group => group.Id == groupId && group.HouseholdId == householdId, token)
                ?? throw new KeyNotFoundException("The group does not belong to this household.");
            var asset = new FinancedAsset(group, name, currencyCode, countryCode,
                purchaseDate, purchasePrice, originalLoanAmount, hasWithdrawalFacility);
            await ApplyAssociationsAsync(context, asset, owners, loanHolderPersonIds,
                includeInHousehold, householdAllocations, token);
            context.FinancedAssets.Add(asset);
            return asset;
        }, cancellationToken);
    }

    public Task<FinancedAsset> SetPersonAssociationsAsync(
        Guid householdId, Guid financedAssetId, IReadOnlyCollection<FinancedAssetShare> owners,
        IReadOnlyCollection<Guid> loanHolderPersonIds, bool includeInHousehold,
        IReadOnlyCollection<FinancedAssetShare> householdAllocations, CancellationToken cancellationToken = default)
    {
        return SerializableTransaction.ExecuteAsync(contextFactory, async (context, token) =>
        {
            var asset = await AssetGraph(context).SingleOrDefaultAsync(
                asset => asset.HouseholdId == householdId && asset.Id == financedAssetId, token)
                ?? throw new KeyNotFoundException("The financed asset does not belong to this household.");
            var oldOwners = asset.Owners.ToArray();
            var oldLoanHolders = asset.LoanHolders.ToArray();
            var oldAllocations = asset.HouseholdAllocations.ToArray();
            await ApplyAssociationsAsync(context, asset, owners, loanHolderPersonIds,
                includeInHousehold, householdAllocations, token);
            context.FinancedAssetOwners.RemoveRange(oldOwners.Except(asset.Owners));
            context.FinancedAssetLoanHolders.RemoveRange(oldLoanHolders.Except(asset.LoanHolders));
            context.FinancedAssetHouseholdAllocations.RemoveRange(oldAllocations.Except(asset.HouseholdAllocations));
            return asset;
        }, cancellationToken);
    }

    public async Task<FinancedAsset> GetAsync(Guid householdId, Guid financedAssetId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // One SQL statement keeps associations and metadata in the same database view.
        return await AssetGraph(context).AsNoTrackingWithIdentityResolution().SingleOrDefaultAsync(
            asset => asset.HouseholdId == householdId && asset.Id == financedAssetId, cancellationToken)
            ?? throw new KeyNotFoundException("The financed asset does not belong to this household.");
    }

    public async Task<FinancedAsset> SetRetiredAsync(
        Guid householdId, Guid financedAssetId, bool isRetired, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var asset = await AssetGraph(context).SingleOrDefaultAsync(
            asset => asset.HouseholdId == householdId && asset.Id == financedAssetId, cancellationToken)
            ?? throw new KeyNotFoundException("The financed asset does not belong to this household.");
        asset.SetRetired(isRetired);
        await context.SaveChangesAsync(cancellationToken);
        return asset;
    }

    private static IQueryable<FinancedAsset> AssetGraph(HearthcastDbContext context) => context.FinancedAssets
        .AsSingleQuery().Include(asset => asset.Group).ThenInclude(group => group.Category)
        .Include(asset => asset.Owners).ThenInclude(owner => owner.Person)
        .Include(asset => asset.LoanHolders).ThenInclude(holder => holder.Person)
        .Include(asset => asset.HouseholdAllocations).ThenInclude(allocation => allocation.Person);

    private static async Task ApplyAssociationsAsync(
        HearthcastDbContext context, FinancedAsset asset, IReadOnlyCollection<FinancedAssetShare> owners,
        IReadOnlyCollection<Guid> loanHolderPersonIds, bool includeInHousehold,
        IReadOnlyCollection<FinancedAssetShare> householdAllocations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(loanHolderPersonIds);
        ArgumentNullException.ThrowIfNull(householdAllocations);
        var ids = owners.Select(share => share.PersonId).Concat(loanHolderPersonIds)
            .Concat(householdAllocations.Select(share => share.PersonId)).Distinct().ToArray();
        var people = await context.People.Where(person => person.HouseholdId == asset.HouseholdId && ids.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, cancellationToken);
        if (people.Count != ids.Length)
        {
            throw new KeyNotFoundException("Every associated person must belong to this household.");
        }
        asset.SetPersonAssociations(
            owners.Select(share => (people[share.PersonId], share.ShareBasisPoints)).ToArray(),
            loanHolderPersonIds.Select(id => people[id]).ToArray(), includeInHousehold,
            householdAllocations.Select(share => (people[share.PersonId], share.ShareBasisPoints)).ToArray());
    }
}
