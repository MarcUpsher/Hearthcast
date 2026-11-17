using Hearthcast.Data.Projections;
using Hearthcast.Data.Projections.Preparation;
using Hearthcast.Api.Health;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Api.Errors;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.TrackedItems;
using Hearthcast.Data.Spending;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var migrateOnly = args.Contains("--migrate");
var backfillRates = args.Contains("--backfill-exchange-rates");
if (migrateOnly && backfillRates) throw new ArgumentException("Run migration and backfill separately.");
var builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--migrate" && argument != "--backfill-exchange-rates").ToArray());
var connectionString = builder.Configuration.GetConnectionString("Hearthcast")
    ?? throw new InvalidOperationException("Configure ConnectionStrings__Hearthcast before starting Hearthcast.");

builder.Services.AddDbContextFactory<HearthcastDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddScoped<HouseholdService>();
builder.Services.AddScoped<ClassificationService>();
builder.Services.AddScoped<SpendingSetupService>();
builder.Services.AddScoped<SpendingRecordService>();
builder.Services.AddScoped<ProjectionPresetService>();
builder.Services.AddScoped<ProjectionPreparationService>();
builder.Services.AddScoped<TrackedItemService>();
builder.Services.AddScoped<FinancedAssetService>();
builder.Services.AddScoped<SnapshotService>();
builder.Services.AddScoped<SnapshotExchangeRateService>();
builder.Services.AddScoped<HistoricalExchangeRateBackfillService>();
builder.Services.AddHttpClient<IExchangeRateProvider, FrankfurterExchangeRateProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.frankfurter.dev/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<SnapshotActivityService>();
builder.Services.AddScoped<SnapshotSummaryService>();
builder.Services.AddScoped<MergedSnapshotSummaryService>();
builder.Services.AddScoped<HistoricalSummarySeriesService>();
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

if (backfillRates)
{
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Console.CancelKeyPress += cancel;
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<HistoricalExchangeRateBackfillService>()
            .RunAsync(cancellation.Token);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = report.Remaining == 0 ? 0 : 2;
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        app.Logger.LogInformation("Exchange-rate backfill cancelled. Run the command again to resume.");
        Environment.ExitCode = 130;
    }
    finally { Console.CancelKeyPress -= cancel; }
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
await app.RunAsync();

// Entry point for WebApplicationFactory integration tests.
public partial class Program;
