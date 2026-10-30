using Hearthcast.Data.Persistence;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hearthcast.Api.Tests.Infrastructure;

internal sealed class ApiTestFactory(PostgresTestDatabase database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        using var context = database.CreateDbContext();
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Hearthcast", context.Database.GetConnectionString());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextFactory<HearthcastDbContext>>();
            services.AddSingleton<IDbContextFactory<HearthcastDbContext>>(database);
            services.AddControllers().AddApplicationPart(typeof(ApiErrorTestController).Assembly);
        });
    }
}
