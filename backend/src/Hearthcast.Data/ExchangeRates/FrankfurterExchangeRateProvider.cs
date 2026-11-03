using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hearthcast.Data.ExchangeRates;

public sealed class FrankfurterExchangeRateProvider(HttpClient client) : IExchangeRateProvider
{
    public string Source => "Frankfurter/ECB";

    public async Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
        DateOnly lookupDate, CancellationToken cancellationToken)
    {
        var url = "v2/providers/ecb/rates?base=" + Uri.EscapeDataString(baseCurrency)
            + "&quotes=" + Uri.EscapeDataString(string.Join(',', quotes))
            + "&date=" + lookupDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rows = await client.GetFromJsonAsync<Row?[]>(url, cancellationToken)
            ?? throw new JsonException("The rate provider returned no data.");
        if (rows.Any(row => row is null)) throw new JsonException("The rate provider returned an invalid row.");
        return rows.Select(row => new ExchangeRateQuote(row!.Base, row.Quote, row.Rate, row.Date)).ToArray();
    }

    private sealed record Row(DateOnly Date, string Base, string Quote, decimal Rate);
}
