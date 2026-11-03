using Hearthcast.Data.Persistence;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hearthcast.Api.Tests.Infrastructure;

internal sealed class ApiTestFactory(PostgresTestDatabase database, IExchangeRateProvider? rateProvider = null) : WebApplicationFactory<Program>
{
    private sealed class UnavailableRates : IExchangeRateProvider
    {
        public string Source => "Test";
        public Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes,
            DateOnly lookupDate, CancellationToken cancellationToken) => throw new HttpRequestException("Offline test provider");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        using var context = database.CreateDbContext();
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Hearthcast", context.Database.GetConnectionString());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IExchangeRateProvider>();
            services.AddSingleton<IExchangeRateProvider>(rateProvider ?? new UnavailableRates());
            services.RemoveAll<IDbContextFactory<HearthcastDbContext>>();
            services.AddSingleton<IDbContextFactory<HearthcastDbContext>>(database);
            services.AddControllers().AddApplicationPart(typeof(ApiErrorTestController).Assembly);
        });
    }
}
