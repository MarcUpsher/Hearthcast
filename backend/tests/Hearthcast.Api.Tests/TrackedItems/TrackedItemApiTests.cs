using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Classification.Contracts;
using Hearthcast.Api.Households.Contracts;
using Hearthcast.Api.TrackedItems.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.TrackedItems;

public sealed class TrackedItemApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new ApiTestFactory(_database);
        _client = _factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task CreationDefaultsToHolderAndListsSavedAllocationAcrossClients()
    {
        var (household, groupId) = await Setup();
        var path = Items(household.Id);
        Assert.Empty((await _client.GetFromJsonAsync<TrackedItemResponse[]>(path))!);
        var result = await _client.PostAsJsonAsync(path, Body(groupId, household.People[0].Id));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var item = (await result.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.Equal(household.Id, item.HouseholdId);
        Assert.Equal(groupId, item.GroupId);
        Assert.Equal(household.People[0].Id, item.HolderPersonId);
        Assert.Equal("Reserve", item.Name);
        Assert.Equal("GBP", item.CurrencyCode);
        Assert.Equal("GB", item.CountryCode);
        Assert.True(item.IncludeInHousehold);
        Assert.False(item.IsRetired);
        Assert.Equal(new AllocationShareResponse(household.People[0].Id, 10_000), Assert.Single(item.Allocations));
        Assert.EndsWith($"{path}/{item.Id}", result.Headers.Location!.ToString());
        using var otherClient = _factory.CreateClient(new() { HandleCookies = false });
        Assert.Equivalent(item, await otherClient.GetFromJsonAsync<TrackedItemResponse>(result.Headers.Location));
        Assert.Equivalent(item, Assert.Single((await otherClient.GetFromJsonAsync<TrackedItemResponse[]>(path))!));
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.TrackedItems.CountAsync());
        Assert.Equal(1, await context.TrackedItemHouseholdAllocations.CountAsync());
    }

    [Fact]
    public async Task ExplicitSharesAreIndependentOfTheActualHolder()
    {
        var (household, groupId) = await Setup();
        var other = await AddPerson(household.Id, "Morgan", true);
        var result = await _client.PostAsJsonAsync(Items(household.Id), new
        {
            groupId, holderPersonId = household.People[0].Id, name = "Fund", currencyCode = "zar",
            countryCode = "za", includeInHousehold = true,
            allocations = new[] { new { personId = household.People[0].Id, shareBasisPoints = 2500 },
                new { personId = other.Id, shareBasisPoints = 7500 } }
        });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var item = (await result.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.Equal(household.People[0].Id, item.HolderPersonId);
        Assert.Equal("ZAR", item.CurrencyCode);
        Assert.Equal("ZA", item.CountryCode);
        Assert.Equal(2, item.Allocations.Count);
        Assert.Contains(new AllocationShareResponse(household.People[0].Id, 2500), item.Allocations);
        Assert.Contains(new AllocationShareResponse(other.Id, 7500), item.Allocations);
        Assert.Equal(10_000, item.Allocations.Sum(share => share.ShareBasisPoints));
    }

    [Fact]
    public async Task ExcludedItemsHaveNoHouseholdAllocationsAndStillAppearInInventory()
    {
        var (household, groupId) = await Setup();
        var result = await _client.PostAsJsonAsync(Items(household.Id), Body(groupId, household.People[0].Id, false));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var item = (await result.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.False(item.IncludeInHousehold);
        Assert.Empty(item.Allocations);
        Assert.Equivalent(item, Assert.Single((await _client.GetFromJsonAsync<TrackedItemResponse[]>(Items(household.Id)))!));
    }

    [Theory]
    [InlineData("groupId", "GroupId")]
    [InlineData("holderPersonId", "HolderPersonId")]
    [InlineData("name", "Name")]
    [InlineData("currencyCode", "CurrencyCode")]
    [InlineData("countryCode", "CountryCode")]
    [InlineData("includeInHousehold", "IncludeInHousehold")]
    public async Task EachRequiredFieldIsValidatedIndependently(string jsonField, string errorField)
    {
        var (household, groupId) = await Setup();
        foreach (var omit in new[] { true, false })
        {
            var body = JsonSerializer.SerializeToNode(Body(groupId, household.People[0].Id),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            if (omit) body.Remove(jsonField);
            else body[jsonField] = null;
            var problem = await AssertProblem(await _client.PostAsJsonAsync(Items(household.Id), body), 400);
            Assert.NotEmpty(problem.GetProperty("errors").GetProperty(errorField).EnumerateArray());
            await AssertNoItems();
        }
    }

    [Fact]
    public async Task ParticipationFlagCannotBeAStringInAnOtherwiseValidRequest()
    {
        var (household, groupId) = await Setup();
        var body = JsonSerializer.SerializeToNode(Body(groupId, household.People[0].Id),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        body["includeInHousehold"] = "false";
        await AssertProblem(await _client.PostAsJsonAsync(Items(household.Id), body), 400);
        await AssertNoItems();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"shareBasisPoints\":10000}]")]
    [InlineData("[{\"personId\":null,\"shareBasisPoints\":10000}]")]
    [InlineData("[{\"personId\":\"00000000-0000-0000-0000-000000000000\",\"shareBasisPoints\":10000}]")]
    [InlineData("[{\"personId\":\"11111111-1111-1111-1111-111111111111\"}]")]
    [InlineData("[{\"personId\":\"11111111-1111-1111-1111-111111111111\",\"shareBasisPoints\":null}]")]
    public async Task MalformedOrEmptyExplicitAllocationsAreRejected(string allocations)
    {
        var (household, groupId) = await Setup();
        var body = JsonSerializer.SerializeToNode(Body(groupId, household.People[0].Id),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        body["allocations"] = System.Text.Json.Nodes.JsonNode.Parse(allocations);
        var problem = await AssertProblem(await _client.PostAsJsonAsync(Items(household.Id), body), 400);
        Assert.NotEmpty(problem.GetProperty("errors").GetProperty("Allocations").EnumerateArray());
        await AssertNoItems();
    }

    [Theory]
    [InlineData(true, "omitted")]
    [InlineData(true, "null")]
    [InlineData(false, "omitted")]
    [InlineData(false, "null")]
    [InlineData(false, "[]")]
    public async Task DefaultAndEmptyAllocationsHaveExplicitSemantics(bool included, string allocations)
    {
        var (household, groupId) = await Setup();
        var body = JsonSerializer.SerializeToNode(Body(groupId, household.People[0].Id, included),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        if (allocations != "omitted")
            body["allocations"] = System.Text.Json.Nodes.JsonNode.Parse(allocations);
        var response = await _client.PostAsJsonAsync(Items(household.Id), body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        if (included)
            Assert.Equal(new AllocationShareResponse(household.People[0].Id, 10_000), Assert.Single(item.Allocations));
        else
            Assert.Empty(item.Allocations);
    }

    [Theory]
    [InlineData("GBP", "G1")]
    [InlineData("GB", "GB")]
    [InlineData("GB£", "GB")]
    public async Task CurrencyAndCountryMustBeAlphabeticCodesOfExpectedLength(string currency, string country)
    {
        var (household, groupId) = await Setup();
        await AssertProblem(await _client.PostAsJsonAsync(Items(household.Id),
            Body(groupId, household.People[0].Id) with { CurrencyCode = currency, CountryCode = country }), 400);
        await AssertNoItems();
    }

    [Theory]
    [InlineData(0, 10_000)]
    [InlineData(1, 9_998)]
    [InlineData(10_001, -1)]
    public async Task InvalidSharesDoNotCreateAnItem(int first, int second)
    {
        var (household, groupId) = await Setup();
        var other = await AddPerson(household.Id, "Morgan", true);
        await AssertProblem(await _client.PostAsJsonAsync(Items(household.Id), new
        {
            groupId, holderPersonId = household.People[0].Id, name = "Fund", currencyCode = "GBP",
            countryCode = "GB", includeInHousehold = true,
            allocations = new[] { new { personId = household.People[0].Id, shareBasisPoints = first },
                new { personId = other.Id, shareBasisPoints = second } }
        }), 400);
        await AssertNoItems();
    }

    [Fact]
    public async Task DuplicateOrExcludedAllocationsAndUnknownPropertiesAreRejected()
    {
        var (household, groupId) = await Setup();
        var holder = household.People[0].Id;
        var path = Items(household.Id);
        await AssertProblem(await _client.PostAsJsonAsync(path, new
        {
            groupId, holderPersonId = holder, name = "Fund", currencyCode = "GBP", countryCode = "GB",
            includeInHousehold = true, allocations = new[] { new { personId = holder, shareBasisPoints = 5000 },
                new { personId = holder, shareBasisPoints = 5000 } }
        }), 400);
        await AssertProblem(await _client.PostAsJsonAsync(path, new
        {
            groupId, holderPersonId = holder, name = "Fund", currencyCode = "GBP", countryCode = "GB",
            includeInHousehold = false, allocations = new[] { new { personId = holder, shareBasisPoints = 10_000 } }
        }), 400);
        await AssertProblem(await _client.PostAsJsonAsync(path, new
        {
            groupId, holderPersonId = holder, name = "Fund", currencyCode = "GBP", countryCode = "GB",
            includeInHousehold = true, amount = 123m
        }), 400);
        await AssertNoItems();
    }

    [Fact]
    public async Task CrossHouseholdReferencesAndNonparticipantAllocationsAreRejected()
    {
        var (household, groupId) = await Setup();
        var (other, otherGroupId) = await Setup();
        var path = Items(household.Id);
        await AssertProblem(await _client.PostAsJsonAsync(path, Body(otherGroupId, household.People[0].Id)), 404);
        await AssertProblem(await _client.PostAsJsonAsync(path, Body(groupId, other.People[0].Id)), 404);
        await AssertProblem(await _client.PostAsJsonAsync(path, new
        {
            groupId, holderPersonId = household.People[0].Id, name = "Fund", currencyCode = "GBP", countryCode = "GB",
            includeInHousehold = true, allocations = new[] { new { personId = other.People[0].Id, shareBasisPoints = 10_000 } }
        }), 404);
        var nonparticipant = await AddPerson(household.Id, "Child", false);
        await AssertProblem(await _client.PostAsJsonAsync(path, new
        {
            groupId, holderPersonId = household.People[0].Id, name = "Fund", currencyCode = "GBP", countryCode = "GB",
            includeInHousehold = true, allocations = new[] { new { personId = nonparticipant.Id, shareBasisPoints = 10_000 } }
        }), 409);
        await AssertNoItems();
    }

    [Fact]
    public async Task NonparticipantCanHoldAnExcludedItemButNotDefaultHouseholdAllocation()
    {
        var (household, groupId) = await Setup();
        var child = await AddPerson(household.Id, "Child", false);
        var path = Items(household.Id);
        await AssertProblem(await _client.PostAsJsonAsync(path, Body(groupId, child.Id)), 409);
        var created = await _client.PostAsJsonAsync(path, Body(groupId, child.Id, false));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = (await created.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        Assert.Equal(child.Id, item.HolderPersonId);
        Assert.False(item.IncludeInHousehold);
    }

    [Fact]
    public async Task ItemIdsAreScopedToTheHouseholdAndMissingHouseholdsAreNotEmptyInventories()
    {
        var (household, groupId) = await Setup();
        var (other, _) = await Setup();
        var created = await _client.PostAsJsonAsync(Items(household.Id), Body(groupId, household.People[0].Id));
        var item = (await created.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
        await AssertProblem(await _client.GetAsync($"{Items(other.Id)}/{item.Id}"), 404);
        await AssertProblem(await _client.GetAsync(Items(Guid.NewGuid())), 404);
        await AssertProblem(await _client.GetAsync($"{Items(household.Id)}/{Guid.NewGuid()}"), 404);
        Assert.Empty((await _client.GetFromJsonAsync<TrackedItemResponse[]>(Items(other.Id)))!);
    }

    private async Task<(HouseholdResponse Household, Guid GroupId)> Setup()
    {
        var home = await _client.PostAsJsonAsync("/api/households", new { name = "Home", firstPersonDisplayName = "Alex" });
        home.EnsureSuccessStatusCode();
        var household = (await home.Content.ReadFromJsonAsync<HouseholdResponse>())!;
        var cat = await _client.PostAsJsonAsync($"/api/households/{household.Id}/categories",
            new { name = "Investments", financialType = "Asset" });
        cat.EnsureSuccessStatusCode();
        var category = (await cat.Content.ReadFromJsonAsync<CategoryResponse>())!;
        var group = await _client.PostAsJsonAsync($"/api/households/{household.Id}/categories/{category.Id}/groups",
            new { name = "Funds" });
        group.EnsureSuccessStatusCode();
        var groupId = (await group.Content.ReadFromJsonAsync<GroupResponse>())!.Id;
        return (household, groupId);
    }

    private async Task<PersonResponse> AddPerson(Guid householdId, string name, bool participates)
    {
        var result = await _client.PostAsJsonAsync($"/api/households/{householdId}/people",
            new { displayName = name, participatesInHouseholdFinances = participates });
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<PersonResponse>())!;
    }

    private async Task AssertNoItems()
    {
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.TrackedItems.ToArrayAsync());
        Assert.Empty(await context.TrackedItemHouseholdAllocations.ToArrayAsync());
    }

    private sealed record CreateBody(Guid GroupId, Guid HolderPersonId, string Name, string CurrencyCode,
        string CountryCode, bool IncludeInHousehold);
    private static CreateBody Body(Guid groupId, Guid holderId, bool included = true) =>
        new(groupId, holderId, "Reserve", "GBP", "GB", included);
    private static string Items(Guid householdId) => $"/api/households/{householdId}/tracked-items";

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("traceId", out _));
        return problem;
    }
}
