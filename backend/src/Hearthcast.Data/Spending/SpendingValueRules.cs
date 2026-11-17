using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

internal static class SpendingValueRules
{
    internal static decimal Amount(decimal value, int minorUnits)
    {
        if (value < 0 || value >= 1_000_000_000_000m || decimal.Round(value, minorUnits) != value)
            throw new InvalidValueException("Use a nonnegative amount below one trillion in the currency's minor units.");
        return value;
    }

    internal static void Units(decimal? quantity, decimal? unitPrice)
    {
        if (quantity.HasValue != unitPrice.HasValue)
            throw new InvalidValueException("Quantity and unit price must both be supplied or both omitted.");
        if (quantity.HasValue)
        {
            if (quantity <= 0 || quantity >= 1_000_000m || decimal.Round(quantity.Value, 6) != quantity)
                throw new InvalidValueException("Use a positive quantity below one million with at most six decimals.");
            SpendingSetupRules.UsualPrice(unitPrice!.Value);
        }
    }

    internal static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }
}
