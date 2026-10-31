using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Households;
using Hearthcast.Data.Classification;
using Hearthcast.Data.TrackedItems;
using Hearthcast.Data.Snapshots;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.Snapshots;

public sealed class SnapshotApiTests : IAsyncLifetime
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
    public async Task CaptureCopiesContextAndPreservesZeroAndUnknownValues()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var categories = new ClassificationService(_database);
        var category = await categories.CreateCategoryAsync(home.Id, "Investments", FinancialType.Asset);
        var group = await categories.CreateGroupAsync(home.Id, category.Id, "Savings");
        var items = new TrackedItemService(_database);
        var item = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "Cash", "GBP", "GB", true);
        await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "Reserve", "GBP", "GB", true);
        var path = $"/api/households/{home.Id}/snapshots";
        Assert.Empty((await _client.GetFromJsonAsync<SnapshotInventoryEntry[]>(path))!);
        var response = await _client.PostAsJsonAsync(path, new {
            snapshotDate = "2023-01-27", datePrecision = 2,
            values = new[] { new { trackedItemId = item.Id, amount = 0m } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        Assert.Equal(new DateOnly(2023, 1, 1), saved.SnapshotDate);
        Assert.Equal(2, saved.ItemSnapshots.Count);
        Assert.Equal(0m, saved.ItemSnapshots.Single(row => row.TrackedItemId == item.Id).Amount);
        Assert.Equal(SnapshotValueOrigin.Confirmed, saved.ItemSnapshots.Single(row => row.TrackedItemId == item.Id).ValueOrigin);
        Assert.Null(saved.ItemSnapshots.Single(row => row.TrackedItemId != item.Id).Amount);
        await categories.RenameCategoryAsync(home.Id, category.Id, "Changed");
        using var independent = _factory.CreateClient();
        Assert.Equivalent(saved, await independent.GetFromJsonAsync<SnapshotResponse>(response.Headers.Location));
        Assert.Equal(saved.Id, Assert.Single((await independent.GetFromJsonAsync<SnapshotInventoryEntry[]>(path))!).Id);
        var foreign = await new HouseholdService(_database).CreateAsync("Other", "Morgan");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/households/{foreign.Id}/snapshots/{saved.Id}")).StatusCode);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"snapshotDate\":null}")]
    [InlineData("{\"snapshotDate\":\"2023-01-01\",\"datePrecision\":99}")]
    [InlineData("{\"snapshotDate\":\"2023-01-01\",\"values\":[null]}")]
    [InlineData("{\"snapshotDate\":\"2023-01-01\",\"householdName\":\"Changed\"}")]
    public async Task InvalidCaptureLeavesNoSnapshots(string json)
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var response = await _client.PostAsJsonAsync($"/api/households/{home.Id}/snapshots", JsonSerializer.Deserialize<JsonElement>(json));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.Snapshots.ToArrayAsync());
    }
    [Fact]
    public async Task MissingHouseholdsAndSnapshotsReturnNotFound()
    {
        var path = $"/api/households/{Guid.NewGuid()}/snapshots";
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(path + "/" + Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync(path, new { snapshotDate = "2023-01-01" })).StatusCode);
    }

    [Theory]
    [InlineData("negative-item")]
    [InlineData("missing-item-id")]
    [InlineData("null-item-id")]
    [InlineData("missing-asset-id")]
    [InlineData("null-asset-id")]
    [InlineData("missing-balance")]
    [InlineData("null-balance")]
    [InlineData("invalid-balance")]
    [InlineData("negative-asset")]
    [InlineData("unsupported-withdrawal")]
    public async Task InvalidNestedValueIsRejectedIndependently(string scenario)
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var person = home.People.Single().Id;
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Assets", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Savings");
        var item = await new TrackedItemService(_database).CreateAsync(home.Id, group.Id, person,
            "Cash", "GBP", "GB", true);
        var asset = await new Hearthcast.Data.FinancedAssets.FinancedAssetService(_database).CreateAsync(
            home.Id, group.Id, "House", "GBP", "GB", null, null, 500m, false,
            [new(person, 10000)], [person], true, [new(person, 10000)]);
        var body = JsonSerializer.SerializeToNode(new {
            snapshotDate = "2023-01-01",
            values = new[] { new { trackedItemId = item.Id, amount = 10m } },
            financedAssetValues = new[] { new { financedAssetId = asset.Id, balance = 1, amount = 10m } }
        })!.AsObject();
        var itemValue = body["values"]![0]!.AsObject();
        var assetValue = body["financedAssetValues"]![0]!.AsObject();
        switch (scenario)
        {
            case "negative-item": itemValue["amount"] = -1; break;
            case "missing-item-id": itemValue.Remove("trackedItemId"); break;
            case "null-item-id": itemValue["trackedItemId"] = null; break;
            case "missing-asset-id": assetValue.Remove("financedAssetId"); break;
            case "null-asset-id": assetValue["financedAssetId"] = null; break;
            case "missing-balance": assetValue.Remove("balance"); break;
            case "null-balance": assetValue["balance"] = null; break;
            case "invalid-balance": assetValue["balance"] = 99; break;
            case "negative-asset": assetValue["amount"] = -1; break;
            case "unsupported-withdrawal": assetValue["balance"] = 3; break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var response = await _client.PostAsJsonAsync($"/api/households/{home.Id}/snapshots", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.Snapshots.ToArrayAsync());
        Assert.Empty(await context.SnapshotPeople.ToArrayAsync());
        Assert.Empty(await context.ItemSnapshots.ToArrayAsync());
        Assert.Empty(await context.FinancedAssetSnapshots.ToArrayAsync());
    }

    [Fact]
    public async Task InventoryOrdersByDescendingDateThenIdAndExcludesOtherHouseholds()
    {
        var households = new HouseholdService(_database);
        var home = await households.CreateAsync("Home", "Alex");
        var other = await households.CreateAsync("Other", "Morgan");
        var path = $"/api/households/{home.Id}/snapshots";
        var created = new List<SnapshotResponse>();
        foreach (var date in new[] { "2023-01-01", "2023-03-01", "2023-02-01", "2023-03-01" })
        {
            var response = await _client.PostAsJsonAsync(path, new { snapshotDate = date });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created.Add((await response.Content.ReadFromJsonAsync<SnapshotResponse>())!);
        }
        var foreignResponse = await _client.PostAsJsonAsync($"/api/households/{other.Id}/snapshots",
            new { snapshotDate = "2024-01-01" });
        Assert.Equal(HttpStatusCode.Created, foreignResponse.StatusCode);
        var foreign = (await foreignResponse.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var expected = created.OrderByDescending(row => row.SnapshotDate).ThenBy(row => row.Id)
            .Select(row => new SnapshotInventoryEntry(row.Id, row.SnapshotDate, row.DatePrecision, row.Revision)).ToArray();

        var inventory = (await _client.GetFromJsonAsync<SnapshotInventoryEntry[]>(path))!;
        Assert.Equal(expected, inventory);
        Assert.Equal(expected, (await _client.GetFromJsonAsync<SnapshotInventoryEntry[]>(path))!);
        Assert.Equal(foreign.Id, Assert.Single((await _client.GetFromJsonAsync<SnapshotInventoryEntry[]>(
            $"/api/households/{other.Id}/snapshots"))!).Id);
    }

    [Fact]
    public async Task FinancedBalancesPersistAndInvalidReferencesOrDuplicatesAreRejectedAtomically()
    {
        var householdService = new HouseholdService(_database);
        var home = await householdService.CreateAsync("Home", "Alex");
        var first = home.People.Single().Id;
        var second = await householdService.CreatePersonAsync(home.Id, "Morgan", true);
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Assets", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Home");
        var asset = await new Hearthcast.Data.FinancedAssets.FinancedAssetService(_database).CreateAsync(
            home.Id, group.Id, "House", "GBP", "GB", null, null, 500m, true,
            [new(first, 5000), new(second.Id, 5000)], [first], true, [new(first, 10000)]);
        var path = $"/api/households/{home.Id}/snapshots";
        var response = await _client.PostAsJsonAsync(path, new {
            snapshotDate = "2023-02-14", financedAssetValues = new[] {
                new { financedAssetId = asset.Id, balance = 1, amount = 1000m },
                new { financedAssetId = asset.Id, balance = 2, amount = 0m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        var row = Assert.Single(saved.FinancedAssetSnapshots);
        Assert.Equal(1000m, row.CurrentValuation);
        Assert.Equal(0m, row.OutstandingLoanBalance);
        Assert.Equal(SnapshotValueOrigin.Confirmed, row.OutstandingLoanBalanceValueOrigin);
        Assert.Equal(2, row.Owners.Count);
        Assert.Equivalent(saved, await _client.GetFromJsonAsync<SnapshotResponse>(response.Headers.Location));
        var invalid = await _client.PostAsJsonAsync(path, new {
            snapshotDate = "2023-03-01", values = new[] { new { trackedItemId = Guid.NewGuid(), amount = 1m } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var duplicate = await _client.PostAsJsonAsync(path, new {
            snapshotDate = "2023-03-01", financedAssetValues = new[] {
                new { financedAssetId = asset.Id, balance = 1, amount = 1m },
                new { financedAssetId = asset.Id, balance = 1, amount = 2m }
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.Snapshots.CountAsync());
    }
}
