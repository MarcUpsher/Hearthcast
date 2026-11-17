using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record MerchantResponse(Guid Id, Guid HouseholdId, string Name, bool IsRetired)
{
    internal static MerchantResponse From(Merchant merchant) =>
        new(merchant.Id, merchant.HouseholdId, merchant.Name, merchant.IsRetired);
}
