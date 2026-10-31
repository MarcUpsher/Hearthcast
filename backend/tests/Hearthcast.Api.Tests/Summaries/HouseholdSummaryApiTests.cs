using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Summaries.Models.Household;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Api.Tests.Summaries;

public sealed class HouseholdSummaryApiTests : IAsyncLifetime
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
    public async Task HouseholdUsesAllocationsAndCountsIncludedHoldingsOnce()
    {
        var data = await CreateSnapshotAsync();
        var response = await _client.GetAsync(data.Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var report = json.Deserialize<SnapshotHouseholdSummary>(JsonSerializerOptions.Web)!;
        Assert.Equal(data.Snapshot.Id, report.SnapshotId);
        Assert.Equal(data.Snapshot.Revision, report.Revision);
        Assert.Equal(data.HouseholdId, report.HouseholdId);
        Assert.Equal(new[] { "GBP", "ZAR" }, report.Currencies.Select(row => row.CurrencyCode));
        Assert.Equal(4, report.People.Count);
        AssertTotals(json, data.FirstPersonId, "ZAR", 610m, false);
        AssertTotals(json, data.SecondPersonId, "ZAR", 450m, true);
        AssertTotals(json, data.SecondPersonId, "GBP", 5m, true);
        Assert.Empty(report.People.Single(row => row.Person.PersonId == data.ChildId).Currencies);
        Assert.Empty(report.People.Single(row => row.Person.DisplayName == "No holdings").Currencies);
        var zarJson = json.GetProperty("currencies").EnumerateArray()
            .Single(row => row.GetProperty("currencyCode").GetString() == "ZAR");
        AssertScopeTotals(zarJson, 1060m, false);
        var zar = report.Currencies.Single(row => row.CurrencyCode == "ZAR");
        Assert.Equal(300m, zar.Totals.OrdinaryAssets.KnownSubtotal);
        Assert.Equal(40m, zar.Totals.OrdinaryDebts.KnownSubtotal);
        Assert.Equal(800m, zar.Totals.FinancedAssetEquity.KnownSubtotal);
        var first = report.People.Single(row => row.Person.PersonId == data.FirstPersonId);
        var firstGroups = first.Currencies.SelectMany(row => row.Categories).SelectMany(row => row.Groups).ToArray();
        var firstAsset = Assert.Single(firstGroups.SelectMany(row => row.FinancedAssets));
        Assert.Equal(5000, firstAsset.AllocationShareBasisPoints);
        Assert.Equal(400m, firstAsset.AttributedEquity);
        Assert.Equal(800m, firstAsset.Asset.CurrentEquity);
        Assert.Equal(2500, firstAsset.Asset.Owners.Single(owner => owner.SnapshotPersonId == first.Person.SnapshotPersonId).ShareBasisPoints);
        var savings = Assert.Single(firstGroups.SelectMany(row => row.Items), row => row.Item.ItemName == "Savings");
        Assert.Equal(120m, savings.ContributingAmount);
        Assert.Equal(6000, savings.AllocationShareBasisPoints);
        Assert.Equal(200m, savings.Item.Balance.Amount);
        var second = report.People.Single(row => row.Person.PersonId == data.SecondPersonId);
        Assert.Equal(second.Person.SnapshotPersonId, savings.Item.HolderSnapshotPersonId);
        var fullItems = report.Currencies.SelectMany(row => row.Categories).SelectMany(row => row.Groups)
            .SelectMany(row => row.Items).ToArray();
        Assert.DoesNotContain(fullItems, row => row.Item.HolderSnapshotPersonId ==
            report.People.Single(person => person.Person.PersonId == data.ChildId).Person.SnapshotPersonId);
        Assert.Equal(200m, Assert.Single(fullItems, row => row.Item.ItemName == "Savings").ContributingAmount);
        foreach (var currency in report.Currencies)
        {
            var peopleTotals = report.People.SelectMany(row => row.Currencies)
                .Where(row => row.CurrencyCode == currency.CurrencyCode).Select(row => row.Totals).ToArray();
            Assert.Equal(currency.Totals.KnownNetPosition, peopleTotals.Sum(row => row.KnownNetPosition));
            Assert.Equal(currency.Totals.OrdinaryAssets.KnownSubtotal, peopleTotals.Sum(row => row.OrdinaryAssets.KnownSubtotal));
            Assert.Equal(currency.Totals.OrdinaryDebts.KnownSubtotal, peopleTotals.Sum(row => row.OrdinaryDebts.KnownSubtotal));
            Assert.Equal(currency.Totals.FinancedAssetEquity.KnownSubtotal, peopleTotals.Sum(row => row.FinancedAssetEquity.KnownSubtotal));
        }
    }

    [Fact]
    public async Task CorrectionsRefreshTotalsWhileSetupEditsPreserveSavedContext()
    {
        var data = await CreateSnapshotAsync();
        var original = (await _client.GetFromJsonAsync<SnapshotHouseholdSummary>(data.Path))!;
        await new ClassificationService(_database).RenameCategoryAsync(data.HouseholdId, data.AssetCategoryId, "Changed setup");
        using var independent = _factory.CreateClient();
        Assert.Equivalent(original, await independent.GetFromJsonAsync<SnapshotHouseholdSummary>(data.Path));
        var correction = await _client.PatchAsJsonAsync(
            $"/api/households/{data.HouseholdId}/snapshots/{data.Snapshot.Id}/values", new {
                expectedRevision = data.Snapshot.Revision,
                values = new[] { new { trackedItemId = data.UnknownId, amount = 50m } }
            });
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        var corrected = (await correction.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var json = await independent.GetFromJsonAsync<JsonElement>(data.Path);
        Assert.Equal(corrected.Revision, json.GetProperty("revision").GetGuid());
        Assert.NotEqual(original.Revision, corrected.Revision);
        AssertTotals(json, data.FirstPersonId, "ZAR", 660m, true);
        AssertTotals(json, data.SecondPersonId, "ZAR", 450m, true);
        AssertScopeTotals(json.GetProperty("currencies").EnumerateArray()
            .Single(row => row.GetProperty("currencyCode").GetString() == "ZAR"), 1110m, true);
        var updated = json.Deserialize<SnapshotHouseholdSummary>(JsonSerializerOptions.Web)!;
        Assert.Contains(updated.People.SelectMany(row => row.Currencies).SelectMany(row => row.Categories),
            category => category.CategoryId == data.AssetCategoryId && category.CategoryName == "Assets");
    }

    [Fact]
    public async Task CarriedHoldingsKeepMissingValuesAndLeaveSourceUnchanged()
    {
        var data = await CreateSnapshotAsync();
        var original = (await _client.GetFromJsonAsync<SnapshotHouseholdSummary>(data.Path))!;
        var nextResponse = await _client.PostAsJsonAsync($"/api/households/{data.HouseholdId}/snapshots/next",
            new { snapshotDate = "2023-02-01" });
        Assert.Equal(HttpStatusCode.Created, nextResponse.StatusCode);
        var next = (await nextResponse.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var json = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/households/{data.HouseholdId}/snapshots/{next.Id}/household-summary");
        AssertTotals(json, data.FirstPersonId, "ZAR", 610m, false);
        var totals = Currency(json, data.FirstPersonId, "ZAR").GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("ordinaryAssets").GetProperty("missingEntryCount").GetInt32());
        Assert.Equal(2, totals.GetProperty("ordinaryAssets").GetProperty("carriedForwardEntryCount").GetInt32());
        Assert.Equal(1, totals.GetProperty("ordinaryDebts").GetProperty("carriedForwardEntryCount").GetInt32());
        Assert.Equal(1, totals.GetProperty("financedAssetEquity").GetProperty("carriedForwardEntryCount").GetInt32());
        Assert.Equivalent(original, await _client.GetFromJsonAsync<SnapshotHouseholdSummary>(data.Path));
    }

    [Fact]
    public async Task EmptyCaptureKeepsPeopleAndMissingOrForeignSnapshotsReturnNotFound()
    {
        var households = new HouseholdService(_database);
        var home = await households.CreateAsync("Home", "Alex");
        var other = await households.CreateAsync("Other", "Morgan");
        var creation = await _client.PostAsJsonAsync($"/api/households/{home.Id}/snapshots", new { snapshotDate = "2023-01-01" });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var snapshot = (await creation.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var empty = (await _client.GetFromJsonAsync<SnapshotHouseholdSummary>(
            $"/api/households/{home.Id}/snapshots/{snapshot.Id}/household-summary"))!;
        Assert.Empty(empty.Currencies);
        Assert.Empty(Assert.Single(empty.People).Currencies);
        foreach (var (householdId, snapshotId) in new[] {
            (other.Id, snapshot.Id), (home.Id, Guid.NewGuid()), (Guid.NewGuid(), snapshot.Id)
        })
        {
            var response = await _client.GetAsync($"/api/households/{householdId}/snapshots/{snapshotId}/household-summary");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(404, problem.GetProperty("status").GetInt32());
            Assert.True(problem.TryGetProperty("traceId", out _));
        }
    }

    private async Task<TestSnapshot> CreateSnapshotAsync()
    {
        var households = new HouseholdService(_database);
        var home = await households.CreateAsync("Home", "Alex");
        var first = home.People.Single().Id;
        home = await households.AddPersonAsync(home.Id, "Morgan", true);
        var second = home.People.Single(row => row.DisplayName == "Morgan").Id;
        home = await households.AddPersonAsync(home.Id, "Child", false);
        var child = home.People.Single(row => row.DisplayName == "Child").Id;
        await households.AddPersonAsync(home.Id, "No holdings", false);
        var classification = new ClassificationService(_database);
        var assets = await classification.CreateCategoryAsync(home.Id, "Assets", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, assets.Id, "Investments");
        var debts = await classification.CreateCategoryAsync(home.Id, "Debts", FinancialType.Debt);
        var loans = await classification.CreateGroupAsync(home.Id, debts.Id, "Loans");
        var items = new TrackedItemService(_database);
        var pension = await items.CreateAsync(home.Id, group.Id, first, "Pension", "ZAR", "ZA", true);
        var savings = await items.CreateAsync(home.Id, group.Id, second, "Savings", "ZAR", "ZA", true,
            [new(first, 6000), new(second, 4000)]);
        var childSavings = await items.CreateAsync(home.Id, group.Id, child, "Savings", "ZAR", "ZA", false);
        var unknown = await items.CreateAsync(home.Id, group.Id, first, "Unknown", "ZAR", "ZA", true);
        var isa = await items.CreateAsync(home.Id, group.Id, first, "ISA", "GBP", "GB", true, [new(second, 10000)]);
        var loan = await items.CreateAsync(home.Id, loans.Id, first, "Loan", "ZAR", "ZA", true, [new(first, 2500), new(second, 7500)]);
        var house = await new FinancedAssetService(_database).CreateAsync(home.Id, group.Id, "House", "ZAR", "ZA",
            null, null, 600m, false, [new(first, 2500), new(second, 7500)], [second], true,
            [new(first, 5000), new(second, 5000)]);
        var creation = await _client.PostAsJsonAsync($"/api/households/{home.Id}/snapshots", new {
            snapshotDate = "2023-01-01",
            values = new[] {
                new { trackedItemId = pension.Id, amount = 100m },
                new { trackedItemId = savings.Id, amount = 200m },
                new { trackedItemId = childSavings.Id, amount = 10m },
                new { trackedItemId = isa.Id, amount = 5m },
                new { trackedItemId = loan.Id, amount = 40m }
            },
            financedAssetValues = new[] {
                new { financedAssetId = house.Id, balance = 1, amount = 1000m },
                new { financedAssetId = house.Id, balance = 2, amount = 200m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var snapshot = (await creation.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        return new(home.Id, first, second, child, assets.Id, unknown.Id, snapshot);
    }

    private static JsonElement Currency(JsonElement report, Guid personId, string currency) =>
        report.GetProperty("people").EnumerateArray()
            .Single(row => row.GetProperty("person").GetProperty("personId").GetGuid() == personId)
            .GetProperty("currencies").EnumerateArray()
            .Single(row => row.GetProperty("currencyCode").GetString() == currency);

    private static void AssertTotals(JsonElement report, Guid personId, string currency, decimal amount, bool complete)
    {
        AssertScopeTotals(Currency(report, personId, currency), amount, complete);
    }

    private static void AssertScopeTotals(JsonElement scope, decimal amount, bool complete)
    {
        var totals = scope.GetProperty("totals");
        Assert.Equal(amount, totals.GetProperty("knownNetPosition").GetDecimal());
        Assert.Equal(complete, totals.GetProperty("isComplete").GetBoolean());
    }

    private sealed record TestSnapshot(Guid HouseholdId, Guid FirstPersonId, Guid SecondPersonId,
        Guid ChildId, Guid AssetCategoryId, Guid UnknownId, SnapshotResponse Snapshot)
    {
        public string Path => $"/api/households/{HouseholdId}/snapshots/{Snapshot.Id}/household-summary";
    }
}
