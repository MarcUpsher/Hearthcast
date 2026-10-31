using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Summaries.Models;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.Summaries;

public sealed class SnapshotSummaryApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new ApiTestFactory(_database);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task SummarySeparatesCurrenciesAndIncludesExcludedHoldingsAndIncompleteTotals()
    {
        var fixture = await CreatePopulatedSnapshotAsync();
        var (householdId, assetCategoryId, savingsGroupId, propertyGroupId,
            _, excludedId, unknownId, snapshot) = fixture;
        var summaryPath = $"/api/households/{householdId}/snapshots/{snapshot.Id}/summary";
        var response = await _client.GetAsync(summaryPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var report = json.Deserialize<SnapshotSummary>(JsonSerializerOptions.Web)!;
        Assert.Equal(snapshot.Id, report.SnapshotId);
        Assert.Equal(snapshot.Revision, report.Revision);
        Assert.Equal(householdId, report.HouseholdId);
        Assert.Equal("Alex", Assert.Single(report.People).DisplayName);
        Assert.Equal(new[] { "GBP", "ZAR" }, report.Currencies.Select(row => row.CurrencyCode));
        var gbp = report.Currencies.Single(row => row.CurrencyCode == "GBP");
        Assert.Equal(175m, gbp.AllRecordedTotals.OrdinaryAssets.KnownSubtotal);
        Assert.Equal(1, gbp.AllRecordedTotals.OrdinaryAssets.MissingEntryCount);
        Assert.Equal(25m, gbp.AllRecordedTotals.OrdinaryDebts.KnownSubtotal);
        Assert.Equal(600m, gbp.AllRecordedTotals.FinancedAssetEquity.KnownSubtotal);
        Assert.Equal(750m, gbp.AllRecordedTotals.KnownNetPosition);
        Assert.False(gbp.AllRecordedTotals.IsComplete);
        Assert.Equal(200m, report.Currencies.Single(row => row.CurrencyCode == "ZAR").AllRecordedTotals.KnownNetPosition);
        var assetCategory = gbp.Categories.Single(row => row.CategoryId == assetCategoryId);
        var savingsGroup = assetCategory.Groups.Single(row => row.GroupId == savingsGroupId);
        Assert.Equal(175m, savingsGroup.AllRecordedTotals.OrdinaryAssets.KnownSubtotal);
        Assert.False(savingsGroup.Items.Single(row => row.TrackedItemId == excludedId).IncludeInHousehold);
        Assert.Null(savingsGroup.Items.Single(row => row.TrackedItemId == unknownId).Balance.Amount);
        var propertyGroup = assetCategory.Groups.Single(row => row.GroupId == propertyGroupId);
        Assert.Equal(600m, Assert.Single(propertyGroup.FinancedAssets).CurrentEquity);
        Assert.Equal(200m, Assert.Single(propertyGroup.FinancedAssets).AvailableToWithdraw.Amount);

        var gbpJson = Currency(json, "GBP");
        AssertJsonTotals(gbpJson, 750m, false);
        Assert.False(gbpJson.GetProperty("allRecordedTotals").GetProperty("ordinaryAssets")
            .GetProperty("isComplete").GetBoolean());
        Assert.True(gbpJson.GetProperty("allRecordedTotals").GetProperty("ordinaryDebts")
            .GetProperty("isComplete").GetBoolean());
        Assert.True(gbpJson.GetProperty("allRecordedTotals").GetProperty("financedAssetEquity")
            .GetProperty("isComplete").GetBoolean());
        AssertJsonTotals(Currency(json, "ZAR"), 200m, true);
        var assetsJson = gbpJson.GetProperty("categories").EnumerateArray()
            .Single(row => row.GetProperty("categoryId").GetGuid() == assetCategoryId);
        AssertJsonTotals(assetsJson, 775m, false);
        AssertJsonTotals(assetsJson.GetProperty("groups").EnumerateArray()
            .Single(row => row.GetProperty("groupId").GetGuid() == savingsGroupId), 175m, false);
        AssertJsonTotals(assetsJson.GetProperty("groups").EnumerateArray()
            .Single(row => row.GetProperty("groupId").GetGuid() == propertyGroupId), 600m, true);
    }

    [Fact]
    public async Task SummaryReflectsCorrectionsAndPreservesSavedMetadata()
    {
        var fixture = await CreatePopulatedSnapshotAsync();
        var snapshots = $"/api/households/{fixture.HouseholdId}/snapshots";
        var summaryPath = $"{snapshots}/{fixture.Snapshot.Id}/summary";
        var report = (await _client.GetFromJsonAsync<SnapshotSummary>(summaryPath))!;

        await new ClassificationService(_database).RenameCategoryAsync(
            fixture.HouseholdId, fixture.AssetCategoryId, "Renamed setup");
        using var independent = _factory.CreateClient();
        Assert.Equivalent(report, await independent.GetFromJsonAsync<SnapshotSummary>(summaryPath));
        var correction = await _client.PatchAsJsonAsync($"{snapshots}/{fixture.Snapshot.Id}/values", new {
            expectedRevision = fixture.Snapshot.Revision,
            values = new[] {
                new { trackedItemId = fixture.CashId, amount = 150m },
                new { trackedItemId = fixture.UnknownId, amount = 50m }
            }
        });
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        var corrected = (await correction.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var response = await independent.GetAsync(summaryPath);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var updated = json.Deserialize<SnapshotSummary>(JsonSerializerOptions.Web)!;
        Assert.Equal(corrected.Revision, updated.Revision);
        Assert.NotEqual(report.Revision, updated.Revision);
        var updatedGbp = updated.Currencies.Single(row => row.CurrencyCode == "GBP");
        Assert.Equal(850m, updatedGbp.AllRecordedTotals.KnownNetPosition);
        Assert.True(updatedGbp.AllRecordedTotals.IsComplete);
        Assert.Equal("Assets", updatedGbp.Categories.Single(row => row.CategoryId == fixture.AssetCategoryId).CategoryName);
        Assert.Equivalent(report.Currencies.Single(row => row.CurrencyCode == "ZAR"),
            updated.Currencies.Single(row => row.CurrencyCode == "ZAR"));
        AssertJsonTotals(Currency(json, "GBP"), 850m, true);
        Assert.True(Currency(json, "GBP").GetProperty("allRecordedTotals").GetProperty("ordinaryAssets")
            .GetProperty("isComplete").GetBoolean());
    }

    [Fact]
    public async Task SummaryCountsCarriedContributionsWithoutChangingSourceSnapshot()
    {
        var fixture = await CreatePopulatedSnapshotAsync();
        var snapshots = $"/api/households/{fixture.HouseholdId}/snapshots";
        var sourcePath = $"{snapshots}/{fixture.Snapshot.Id}/summary";
        var source = (await _client.GetFromJsonAsync<SnapshotSummary>(sourcePath))!;
        var nextResponse = await _client.PostAsJsonAsync(snapshots + "/next", new { snapshotDate = "2023-02-01" });
        Assert.Equal(HttpStatusCode.Created, nextResponse.StatusCode);
        var next = (await nextResponse.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var carried = (await _client.GetFromJsonAsync<SnapshotSummary>($"{snapshots}/{next.Id}/summary"))!;
        var carriedGbp = carried.Currencies.Single(row => row.CurrencyCode == "GBP");
        Assert.Equal(2, carriedGbp.AllRecordedTotals.OrdinaryAssets.CarriedForwardEntryCount);
        Assert.Equal(1, carriedGbp.AllRecordedTotals.OrdinaryAssets.MissingEntryCount);
        Assert.Equal(1, carriedGbp.AllRecordedTotals.OrdinaryDebts.CarriedForwardEntryCount);
        Assert.Equal(1, carriedGbp.AllRecordedTotals.FinancedAssetEquity.CarriedForwardEntryCount);
        Assert.Equal(750m, carriedGbp.AllRecordedTotals.KnownNetPosition);
        using var independent = _factory.CreateClient();
        Assert.Equivalent(source, await independent.GetFromJsonAsync<SnapshotSummary>(sourcePath));
    }

    [Fact]
    public async Task SummaryOfEmptyCaptureKeepsRosterAndEnforcesHouseholdScope()
    {
        var households = new HouseholdService(_database);
        var home = await households.CreateAsync("Home", "Alex");
        var other = await households.CreateAsync("Other", "Morgan");
        var creation = await _client.PostAsJsonAsync($"/api/households/{home.Id}/snapshots", new { snapshotDate = "2023-01-01" });
        creation.EnsureSuccessStatusCode();
        var snapshot = (await creation.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var path = $"/api/households/{home.Id}/snapshots/{snapshot.Id}/summary";
        var empty = (await _client.GetFromJsonAsync<SnapshotSummary>(path))!;
        Assert.Single(empty.People);
        Assert.Empty(empty.Currencies);
        foreach (var missingPath in new[] {
            $"/api/households/{other.Id}/snapshots/{snapshot.Id}/summary",
            $"/api/households/{home.Id}/snapshots/{Guid.NewGuid()}/summary",
            $"/api/households/{Guid.NewGuid()}/snapshots/{snapshot.Id}/summary"
        })
        {
            var response = await _client.GetAsync(missingPath);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(404, problem.GetProperty("status").GetInt32());
            Assert.True(problem.TryGetProperty("traceId", out _));
        }
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.Snapshots.CountAsync());
        Assert.Equal(snapshot.Revision, (await context.Snapshots.SingleAsync()).Revision);
    }

    private async Task<PopulatedSnapshot> CreatePopulatedSnapshotAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var person = home.People.Single().Id;
        var classification = new ClassificationService(_database);
        var assets = await classification.CreateCategoryAsync(home.Id, "Assets", FinancialType.Asset);
        var debts = await classification.CreateCategoryAsync(home.Id, "Debts", FinancialType.Debt);
        var savings = await classification.CreateGroupAsync(home.Id, assets.Id, "Savings");
        var loans = await classification.CreateGroupAsync(home.Id, debts.Id, "Loans");
        var property = await classification.CreateGroupAsync(home.Id, assets.Id, "Property");
        var items = new TrackedItemService(_database);
        var cash = await items.CreateAsync(home.Id, savings.Id, person, "Cash", "GBP", "GB", true);
        var excluded = await items.CreateAsync(home.Id, savings.Id, person, "Personal reserve", "GBP", "GB", false);
        var unknown = await items.CreateAsync(home.Id, savings.Id, person, "Unknown reserve", "GBP", "GB", true);
        var loan = await items.CreateAsync(home.Id, loans.Id, person, "Loan", "GBP", "GB", true);
        var rand = await items.CreateAsync(home.Id, savings.Id, person, "SA fund", "ZAR", "ZA", true);
        var house = await new FinancedAssetService(_database).CreateAsync(home.Id, property.Id, "House", "GBP", "GB",
            null, null, 600m, true, [new(person, 10000)], [person], true, [new(person, 10000)]);
        var snapshots = $"/api/households/{home.Id}/snapshots";
        var creation = await _client.PostAsJsonAsync(snapshots, new {
            snapshotDate = "2023-01-01",
            values = new[] {
                new { trackedItemId = cash.Id, amount = 100m },
                new { trackedItemId = excluded.Id, amount = 75m },
                new { trackedItemId = loan.Id, amount = 25m },
                new { trackedItemId = rand.Id, amount = 200m }
            },
            financedAssetValues = new[] {
                new { financedAssetId = house.Id, balance = 1, amount = 1000m },
                new { financedAssetId = house.Id, balance = 2, amount = 400m },
                new { financedAssetId = house.Id, balance = 3, amount = 200m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var snapshot = (await creation.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        return new(home.Id, assets.Id, savings.Id, property.Id, cash.Id, excluded.Id, unknown.Id, snapshot);
    }

    private static JsonElement Currency(JsonElement report, string currencyCode) =>
        report.GetProperty("currencies").EnumerateArray()
            .Single(row => row.GetProperty("currencyCode").GetString() == currencyCode);

    private static void AssertJsonTotals(JsonElement scope, decimal knownNetPosition, bool isComplete)
    {
        var totals = scope.GetProperty("allRecordedTotals");
        Assert.Equal(knownNetPosition, totals.GetProperty("knownNetPosition").GetDecimal());
        Assert.Equal(isComplete, totals.GetProperty("isComplete").GetBoolean());
    }

    private sealed record PopulatedSnapshot(Guid HouseholdId, Guid AssetCategoryId, Guid SavingsGroupId,
        Guid PropertyGroupId, Guid CashId, Guid ExcludedId, Guid UnknownId, SnapshotResponse Snapshot);
}
