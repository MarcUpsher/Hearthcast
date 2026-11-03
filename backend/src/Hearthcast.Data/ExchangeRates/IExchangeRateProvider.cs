namespace Hearthcast.Data.ExchangeRates;

public sealed record ExchangeRateQuote(string BaseCurrency, string QuoteCurrency, decimal Rate, DateOnly EffectiveDate);

public interface IExchangeRateProvider
{
    string Source { get; }
    Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
        DateOnly lookupDate, CancellationToken cancellationToken);
}
