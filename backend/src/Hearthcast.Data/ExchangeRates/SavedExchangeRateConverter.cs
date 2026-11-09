using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.ExchangeRates;

internal sealed class SavedExchangeRateConverter(string? baseCurrency, IReadOnlyCollection<SnapshotExchangeRate> quotes)
{
    public decimal? ResolveRate(string sourceCurrency, string targetCurrency)
    {
        if (sourceCurrency == targetCurrency) return 1m;
        if (BaseRate(sourceCurrency) is not { } source || BaseRate(targetCurrency) is not { } target)
            return null;
        var factor = target / source;
        if (factor <= 0)
            throw new InvalidValueException("The saved exchange rate exceeds supported numeric precision.");
        return factor;
    }

    public static decimal Convert(decimal amount, decimal factor)
    {
        var converted = amount * factor;
        if (amount != 0 && converted == 0)
            throw new InvalidValueException("The converted amount exceeds supported numeric precision.");
        return converted;
    }

    private decimal? BaseRate(string currency) => currency == baseCurrency ? 1m
        : quotes.SingleOrDefault(quote => quote.BaseCurrency == baseCurrency && quote.QuoteCurrency == currency)?.Rate;
}
