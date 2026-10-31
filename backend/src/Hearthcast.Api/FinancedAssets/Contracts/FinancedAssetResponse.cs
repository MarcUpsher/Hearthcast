using Hearthcast.Data.FinancedAssets;

namespace Hearthcast.Api.FinancedAssets.Contracts;

public sealed record FinancedAssetResponse(Guid Id, Guid HouseholdId, Guid GroupId, string Name,
    string CurrencyCode, string CountryCode, DateOnly? PurchaseDate, decimal? PurchasePrice,
    decimal OriginalLoanAmount, bool HasWithdrawalFacility, bool IncludeInHousehold, bool IsRetired,
    IReadOnlyList<FinancedAssetShareResponse> Owners, IReadOnlyList<Guid> LoanHolderPersonIds,
    IReadOnlyList<FinancedAssetShareResponse> HouseholdAllocations)
{
    internal static FinancedAssetResponse From(FinancedAsset asset) => new(asset.Id, asset.HouseholdId, asset.GroupId,
        asset.Name, asset.CurrencyCode, asset.CountryCode, asset.PurchaseDate, asset.PurchasePrice,
        asset.OriginalLoanAmount, asset.HasWithdrawalFacility, asset.IncludeInHousehold, asset.IsRetired,
        asset.Owners.OrderBy(row => row.PersonId).Select(row => new FinancedAssetShareResponse(row.PersonId, row.ShareBasisPoints)).ToArray(),
        asset.LoanHolders.Select(row => row.PersonId).OrderBy(id => id).ToArray(),
        asset.HouseholdAllocations.OrderBy(row => row.PersonId).Select(row => new FinancedAssetShareResponse(row.PersonId, row.ShareBasisPoints)).ToArray());
}
