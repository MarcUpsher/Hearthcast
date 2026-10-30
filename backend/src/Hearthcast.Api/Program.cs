using Hearthcast.Api.Health;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.TrackedItems;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var migrateOnly = args.Contains("--migrate");
var builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--migrate").ToArray());
var connectionString = builder.Configuration.GetConnectionString("Hearthcast")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Hearthcast before starting Hearthcast.");

builder.Services.AddDbContextFactory<HearthcastDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<HouseholdService>();
builder.Services.AddScoped<ClassificationService>();
builder.Services.AddScoped<TrackedItemService>();
builder.Services.AddScoped<FinancedAssetService>();
builder.Services.AddScoped<SnapshotService>();
builder.Services.AddScoped<SnapshotSummaryService>();
builder.Services.AddScoped<IndividualHoldingsSummaryService>();
builder.Services.AddScoped<HouseholdSummaryService>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

await using var app = builder.Build();
if (migrateOnly)
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<HearthcastDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.MigrateAsync();
    app.Logger.LogInformation("Database migrations completed.");
    return;
}

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
await app.RunAsync();
