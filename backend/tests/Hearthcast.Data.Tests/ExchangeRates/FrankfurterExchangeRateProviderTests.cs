using System.Net;
using Hearthcast.Data.ExchangeRates;

namespace Hearthcast.Data.Tests;

public sealed class FrankfurterExchangeRateProviderTests
{
    [Fact]
    public async Task RequestsEcbRatesForLookupDateAndPreservesWeekendPublicationDate()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/") };
        var provider = new FrankfurterExchangeRateProvider(client);
        var rates = await provider.GetAsync("GBP", ["ZAR"], new DateOnly(2023, 1, 1), default);
        var rate = Assert.Single(rates);
        Assert.Equal(new DateOnly(2022, 12, 30), rate.EffectiveDate);
        Assert.Equal(20.406m, rate.Rate);
        Assert.Equal("GBP", rate.BaseCurrency);
        Assert.Equal("ZAR", rate.QuoteCurrency);
        Assert.Equal("/v2/providers/ecb/rates?base=GBP&quotes=ZAR&date=2023-01-01", handler.Uri!.PathAndQuery);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"date":"2022-12-30","base":"GBP","quote":"ZAR","rate":20.406}]""",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
