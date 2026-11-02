using System.Net;
using System.Net.Http.Json;
using System.Text;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Api.TrackedItems.Contracts;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Api.Tests.TrackedItems;

public sealed class TrackedItemMetadataApiTests : IAsyncLifetime
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
    public async Task MetadataPersistsWithoutChangingHistoricalSnapshotsAndIsCopiedIntoFutureCarries()
    {
        var (home, group, person) = await Setup();
        var service = new TrackedItemService(_database);
        var item = await service.CreateAsync(home, group, person, "Retirement", "GBP", "GB", true);
        var path = $"/api/households/{home}/tracked-items/{item.Id}";
        var snapshots = new SnapshotService(_database);
        var old = await snapshots.CreateAsync(home, new DateOnly(2020, 1, 1), [new(item.Id, 100m)], DatePrecision.Month);
        await service.SetRetiredAsync(home, item.Id, true);
        var before = (await _client.GetFromJsonAsync<TrackedItemResponse>(path))!;
        var response = await _client.PutAsJsonAsync(path + "/metadata", new { institutionName = "  Example platform  ", fundName = "  Equity fund  " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changed = (await response.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.Equivalent(before with { InstitutionName = "Example platform", FundName = "Equity fund" }, changed);
        Assert.True(changed.IsRetired);
        using var independent = _factory.CreateClient();
        Assert.Equivalent(changed, await independent.GetFromJsonAsync<TrackedItemResponse>(path));
        var historical = await snapshots.GetAsync(home, old.Id);
        Assert.Null(Assert.Single(historical.ItemSnapshots).InstitutionName);
        Assert.Null(Assert.Single(historical.ItemSnapshots).FundName);
        Assert.Equal(old.Revision, historical.Revision);
        await service.SetRetiredAsync(home, item.Id, false);
        var next = await snapshots.CreateNextAsync(home, new DateOnly(2020, 2, 1), datePrecision: DatePrecision.Month, sourceSnapshotId: old.Id);
        var entry = Assert.Single(next.ItemSnapshots);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal("Example platform", entry.InstitutionName);
        Assert.Equal("Equity fund", entry.FundName);
        Assert.Equal(100m, entry.Amount);
        Assert.Equal(new DateOnly(2020, 1, 1), entry.LastConfirmedDate);
        await service.SetMetadataAsync(home, item.Id, "Changed platform", "Changed fund");
        var corrected = await snapshots.CorrectValuesAsync(home, next.Id, next.Revision, [new(item.Id, 110m)]);
        var saved = await snapshots.GetAsync(home, corrected.Id);
        Assert.Equal("Equity fund", Assert.Single(saved.ItemSnapshots).FundName);
        Assert.Equal("Example platform", Assert.Single(saved.ItemSnapshots).InstitutionName);
        var full = await new SnapshotSummaryService(_database).GetAsync(home, next.Id);
        var summary = Assert.Single(Assert.Single(Assert.Single(full.Currencies).Categories).Groups).Items.Single();
        Assert.Equal("Equity fund", summary.FundName);
        var individuals = await new IndividualHoldingsSummaryService(_database).GetAsync(home, next.Id);
        var owned = individuals.People.Single().Currencies.Single().Categories.Single().Groups.Single().Items.Single();
        Assert.Equal("Example platform", owned.InstitutionName);
        var household = await new HouseholdSummaryService(_database).GetAsync(home, next.Id);
        Assert.Equal("Equity fund", household.Currencies.Single().Categories.Single().Groups.Single().Items.Single().Item.FundName);
        var cleared = await _client.PutAsJsonAsync(path + "/metadata", new { institutionName = "  ", fundName = (string?)null });
        var empty = (await cleared.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.Null(empty.InstitutionName);
        Assert.Null(empty.FundName);
        Assert.Equal("Equity fund", (await snapshots.GetAsync(home, next.Id)).ItemSnapshots.Single().FundName);
        var other = await new HouseholdService(_database).CreateAsync("Other", "Morgan");
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync($"/api/households/{other.Id}/tracked-items/{item.Id}/metadata", new { institutionName = "Foreign", fundName = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task CreationAcceptsOptionalMetadata()
    {
        var (home, group, person) = await Setup();
        var response = await _client.PostAsJsonAsync($"/api/households/{home}/tracked-items", new
        {
            groupId = group,
            holderPersonId = person,
            name = "Investment",
            currencyCode = "GBP",
            countryCode = "GB",
            includeInHousehold = true,
            institutionName = " Platform ",
            fundName = " Fund "
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.Equal("Platform", item.InstitutionName);
        Assert.Equal("Fund", item.FundName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"institutionName\":\"Platform\"}")]
    [InlineData("{\"institutionName\":null,\"fundName\":null,\"name\":\"Unexpected\"}")]
    [InlineData("{\"institutionName\":\"New platform\",\"fundName\":\"bad\\u0000text\"}")]
    public async Task InvalidReplacementLeavesBothFieldsUnchanged(string json)
    {
        var (home, group, person) = await Setup();
        var item = await new TrackedItemService(_database).CreateAsync(home, group, person, "Item", "GBP", "GB", true,
            institutionName: "Original platform", fundName: "Original fund");
        var path = $"/api/households/{home}/tracked-items/{item.Id}";
        var before = await _client.GetStringAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsync(path + "/metadata", new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(before, await _client.GetStringAsync(path));
    }

    [Fact]
    public async Task CombinedDetailsUpdatePreservesSetupAndHistoryAndCopiesBothIntoFutureSnapshots()
    {
        var (home, group, person) = await Setup();
        var service = new TrackedItemService(_database);
        var item = await service.CreateAsync(home, group, person, "Original", "GBP", "GB", true,
            institutionName: "Original platform", fundName: "Original fund");
        var path = $"/api/households/{home}/tracked-items/{item.Id}";
        var snapshots = new SnapshotService(_database);
        var original = await snapshots.CreateAsync(home, new DateOnly(2020, 1, 1), [new(item.Id, 100m)]);
        await service.SetRetiredAsync(home, item.Id, true);
        var before = (await _client.GetFromJsonAsync<TrackedItemResponse>(path))!;

        var response = await _client.PutAsJsonAsync(path + "/details",
            new { name = " Renamed ", institutionName = " New platform ", fundName = "  ", includeInHousehold = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.Equivalent(before with { Name = "Renamed", InstitutionName = "New platform", FundName = null }, updated);
        Assert.Equivalent(updated, await _client.GetFromJsonAsync<TrackedItemResponse>(path));

        var historical = await snapshots.GetAsync(home, original.Id);
        var saved = Assert.Single(historical.ItemSnapshots);
        Assert.Equal("Original", saved.ItemName);
        Assert.Equal("Original platform", saved.InstitutionName);
        Assert.Equal("Original fund", saved.FundName);
        Assert.Equal(original.Revision, historical.Revision);

        await service.SetRetiredAsync(home, item.Id, false);
        var next = await snapshots.CreateNextAsync(home, new DateOnly(2020, 2, 1));
        var carried = Assert.Single(next.ItemSnapshots);
        Assert.Equal(item.Id, carried.TrackedItemId);
        Assert.Equal("Renamed", carried.ItemName);
        Assert.Equal("New platform", carried.InstitutionName);
        Assert.Null(carried.FundName);
        Assert.Equal(100m, carried.Amount);

        var other = await new HouseholdService(_database).CreateAsync("Other", "Morgan");
        var payload = new { name = "Wrong", institutionName = (string?)null, fundName = (string?)null, includeInHousehold = true };
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync(
            $"/api/households/{other.Id}/tracked-items/{item.Id}/details", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync(
            $"/api/households/{home}/tracked-items/{Guid.NewGuid()}/details", payload)).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"Changed\"}")]
    [InlineData("{\"name\":null,\"institutionName\":null,\"fundName\":null}")]
    [InlineData("{\"name\":\"  \",\"institutionName\":null,\"fundName\":null}")]
    [InlineData("{\"name\":\"Changed\",\"institutionName\":null,\"fundName\":null,\"isRetired\":true}")]
    [InlineData("{\"name\":\"Changed\",\"institutionName\":\"Changed\",\"fundName\":\"bad\\u0000text\"}")]
    public async Task RejectedCombinedUpdateLeavesNameAndMetadataUnchanged(string json)
    {
        var (home, group, person) = await Setup();
        var item = await new TrackedItemService(_database).CreateAsync(home, group, person, "Original", "GBP", "GB", true,
            institutionName: "Original platform", fundName: "Original fund");
        var path = $"/api/households/{home}/tracked-items/{item.Id}";
        var before = await _client.GetStringAsync(path);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        payload["includeInHousehold"] = true;
        var response = await _client.PutAsync(path + "/details", new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await _client.GetStringAsync(path));
    }

    private async Task<(Guid, Guid, Guid)> Setup()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Investments", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Retirement");
        return (home.Id, group.Id, home.People.Single().Id);
    }
}
