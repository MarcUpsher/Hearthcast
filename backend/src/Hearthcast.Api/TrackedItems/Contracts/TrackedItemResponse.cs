using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Api.TrackedItems.Contracts;

public sealed record AllocationShareResponse(Guid PersonId, int ShareBasisPoints);

public sealed record TrackedItemResponse(
    Guid Id, Guid HouseholdId, Guid GroupId, Guid HolderPersonId, string Name,
    string CurrencyCode, string CountryCode, bool IncludeInHousehold, bool IsRetired,
    IReadOnlyList<AllocationShareResponse> Allocations, string? InstitutionName = null, string? FundName = null)
{
    internal static TrackedItemResponse From(TrackedItem item) => new(
        item.Id, item.HouseholdId, item.GroupId, item.HolderPersonId, item.Name,
        item.CurrencyCode, item.CountryCode, item.IncludeInHousehold, item.IsRetired,
        item.HouseholdAllocations.OrderBy(share => share.PersonId)
            .Select(share => new AllocationShareResponse(share.PersonId, share.ShareBasisPoints)).ToArray(),
        item.InstitutionName, item.FundName);
}
