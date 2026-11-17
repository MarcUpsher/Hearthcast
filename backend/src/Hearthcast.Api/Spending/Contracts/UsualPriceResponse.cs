using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record UsualPriceResponse(Guid Id, Guid HouseholdId, Guid SpendingItemId, Guid MerchantId,
    string CurrencyCode, string UnitKey, string UnitLabel, decimal UsualUnitPrice, DateTimeOffset UpdatedAtUtc, Guid Revision)
{
    internal static UsualPriceResponse From(SpendingItemMerchantPrice price) => new(price.Id, price.HouseholdId,
        price.SpendingItemId, price.MerchantId, price.CurrencyCode, price.UnitKey, price.UnitLabel,
        // PostgreSQL persists microseconds; use that precision before and after reload.
        price.UsualUnitPrice, price.UpdatedAtUtc.AddTicks(-(price.UpdatedAtUtc.Ticks % 10)), price.Revision);
}
