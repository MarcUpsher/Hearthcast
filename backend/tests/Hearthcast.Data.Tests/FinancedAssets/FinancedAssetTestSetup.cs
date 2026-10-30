using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;

namespace Hearthcast.Data.Tests.FinancedAssets;

internal sealed record FinancedAssetTestSetup(Guid HouseholdId, Guid GroupId, Guid FirstPersonId, Guid SecondPersonId)
{
    public static async Task<FinancedAssetTestSetup> CreateAsync(PostgresTestDatabase database,
        bool secondParticipates = true, FinancialType financialType = FinancialType.Asset)
    {
        var data = await TrackedItemTestSetup.CreateAsync(database, secondParticipates, financialType);
        return new(data.HouseholdId, data.GroupId, data.HolderPersonId, data.OtherPersonId);
    }

    public Task<FinancedAsset> CreateAssetAsync(FinancedAssetService service,
        IReadOnlyCollection<FinancedAssetShare>? owners = null, IReadOnlyCollection<Guid>? loanHolders = null,
        bool include = true, IReadOnlyCollection<FinancedAssetShare>? allocations = null,
        decimal? purchasePrice = 1_100_000m, decimal originalLoan = 1_000_000m,
        string name = "Shared home", string currency = "ZAR", string country = "ZA",
        DateOnly? purchaseDate = null, bool withdrawal = true) =>
        service.CreateAsync(HouseholdId, GroupId, name, currency, country,
            purchaseDate, purchasePrice, originalLoan, withdrawal,
            owners ?? [new(FirstPersonId, 5_000), new(SecondPersonId, 5_000)],
            loanHolders ?? [SecondPersonId], include,
            allocations ?? (include ? [new(FirstPersonId, 5_000), new(SecondPersonId, 5_000)] : []));
}
