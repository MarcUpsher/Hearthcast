using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hearthcast.Api.Classification.Contracts;
using Hearthcast.Api.Households.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.Classification;

public sealed class ClassificationApiTests : IAsyncLifetime
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

    [Theory]
    [InlineData("Asset", FinancialType.Asset)]
    [InlineData("asset", FinancialType.Asset)]
    [InlineData("Debt", FinancialType.Debt)]
    [InlineData("DEBT", FinancialType.Debt)]
    public async Task CreateReadListAndRenamePreserveTypeAndRelationships(string type, FinancialType expected)
    {
        var household = await CreateHousehold();
        var categories = Categories(household.Id);
        Assert.Empty((await _client.GetFromJsonAsync<CategoryResponse[]>(categories))!);
        var created = await _client.PostAsJsonAsync(categories, new { name = "  Category  ", financialType = type });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var responseJson = await created.Content.ReadFromJsonAsync<JsonElement>();
        var category = responseJson.Deserialize<CategoryResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("Category", category.Name);
        Assert.Equal(expected, category.FinancialType);
        Assert.Equal(household.Id, category.HouseholdId);
        Assert.EndsWith($"{categories}/{category.Id}", created.Headers.Location!.ToString());
        Assert.Equal(expected.ToString(), responseJson.GetProperty("financialType").GetString());
        Assert.Equal(category, await _client.GetFromJsonAsync<CategoryResponse>(created.Headers.Location));
        Assert.Equal(category, Assert.Single((await _client.GetFromJsonAsync<CategoryResponse[]>(categories))!));

        var groups = $"{categories}/{category.Id}/groups";
        Assert.Empty((await _client.GetFromJsonAsync<GroupResponse[]>(groups))!);
        var groupCreation = await _client.PostAsJsonAsync(groups, new { name = "  Group  " });
        Assert.Equal(HttpStatusCode.Created, groupCreation.StatusCode);
        var group = (await groupCreation.Content.ReadFromJsonAsync<GroupResponse>())!;
        Assert.Equal("Group", group.Name);
        Assert.Equal(household.Id, group.HouseholdId);
        Assert.Equal(category.Id, group.CategoryId);
        Assert.EndsWith($"{groups}/{group.Id}", groupCreation.Headers.Location!.ToString());
        Assert.Equal(group, await _client.GetFromJsonAsync<GroupResponse>(groupCreation.Headers.Location));
        Assert.Equal(group, Assert.Single((await _client.GetFromJsonAsync<GroupResponse[]>(groups))!));

        var renamedCategory = await _client.PutAsJsonAsync(created.Headers.Location, new { name = "  Renamed category  " });
        Assert.Equal(HttpStatusCode.OK, renamedCategory.StatusCode);
        var expectedCategory = category with { Name = "Renamed category" };
        Assert.Equal(expectedCategory, await renamedCategory.Content.ReadFromJsonAsync<CategoryResponse>());
        var renamedGroup = await _client.PutAsJsonAsync(groupCreation.Headers.Location, new { name = "  Renamed group  " });
        Assert.Equal(HttpStatusCode.OK, renamedGroup.StatusCode);
        var expectedGroup = group with { Name = "Renamed group" };
        Assert.Equal(expectedGroup, await renamedGroup.Content.ReadFromJsonAsync<GroupResponse>());
        using var otherClient = _factory.CreateClient(new() { HandleCookies = false });
        Assert.Equal(expectedCategory, await otherClient.GetFromJsonAsync<CategoryResponse>(created.Headers.Location));
        Assert.Equal(expectedGroup, await otherClient.GetFromJsonAsync<GroupResponse>(groupCreation.Headers.Location));
        await AssertProblem(await _client.DeleteAsync(created.Headers.Location), 405);
        await AssertProblem(await _client.DeleteAsync(groupCreation.Headers.Location), 405);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"name\":\"Category\"}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":null}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":0}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":1}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":3}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":\"1\"}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":\"3\"}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":\"Other\"}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":\"Asset, Debt\"}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":true}")]
    [InlineData("{\"name\":\"Category\",\"financialType\":\"Asset\",\"householdId\":\"ignored\"}")]
    public async Task CategoryCreationRequiresAnExplicitDefinedTextType(string body)
    {
        var household = await CreateHousehold();
        var problem = await AssertProblem(await _client.PostAsync(Categories(household.Id), Json(body)), 400);
        Assert.True(problem.TryGetProperty("errors", out _));
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.Categories.ToArrayAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\" \"}")]
    [InlineData("{\"name\":42}")]
    [InlineData("{\"name\":\"Bad\\u0000name\"}")]
    public async Task AllNameMutationsRejectInvalidInputAndPreserveSavedData(string body)
    {
        var household = await CreateHousehold();
        var category = await CreateCategory(household.Id);
        var categoryPath = $"{Categories(household.Id)}/{category.Id}";
        var group = await CreateGroup(categoryPath);
        var groupPath = $"{categoryPath}/groups/{group.Id}";
        // Category creation adds a type to the same invalid name body.
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body)!;
        var categoryBody = new Dictionary<string, object?> { ["financialType"] = "Asset" };
        foreach (var pair in values) categoryBody[pair.Key] = pair.Value;
        await AssertProblem(await _client.PostAsJsonAsync(Categories(household.Id), categoryBody), 400);
        await AssertProblem(await _client.PutAsync(categoryPath, Json(body)), 400);
        await AssertProblem(await _client.PostAsync(categoryPath + "/groups", Json(body)), 400);
        await AssertProblem(await _client.PutAsync(groupPath, Json(body)), 400);
        Assert.Equal(category, await _client.GetFromJsonAsync<CategoryResponse>(categoryPath));
        Assert.Equal(group, await _client.GetFromJsonAsync<GroupResponse>(groupPath));
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.Categories.CountAsync());
        Assert.Equal(1, await context.ItemGroups.CountAsync());
    }

    [Fact]
    public async Task RequiredFieldsHaveNamedValidationErrors()
    {
        var household = await CreateHousehold();
        var categories = Categories(household.Id);
        var problem = await AssertProblem(await _client.PostAsync(categories, Json("{}")), 400);
        AssertField(problem, "Name");
        AssertField(problem, "FinancialType");
        var category = await CreateCategory(household.Id);
        var path = $"{categories}/{category.Id}";
        AssertField(await AssertProblem(await _client.PutAsync(path, Json("{}")), 400), "Name");
        AssertField(await AssertProblem(await _client.PostAsync(path + "/groups", Json("{}")), 400), "Name");
        var group = await CreateGroup(path);
        AssertField(await AssertProblem(await _client.PutAsync(path + $"/groups/{group.Id}", Json("{}")), 400), "Name");
    }

    [Fact]
    public async Task RenameRejectsTypeAndRelationshipChangesRatherThanSilentlyIgnoringThem()
    {
        var household = await CreateHousehold();
        var category = await CreateCategory(household.Id);
        var path = $"{Categories(household.Id)}/{category.Id}";
        var group = await CreateGroup(path);
        await AssertProblem(await _client.PutAsJsonAsync(path, new { name = "Changed", financialType = "Debt" }), 400);
        await AssertProblem(await _client.PutAsJsonAsync(path + $"/groups/{group.Id}", new { name = "Changed", categoryId = Guid.NewGuid() }), 400);
        Assert.Equal(category, await _client.GetFromJsonAsync<CategoryResponse>(path));
        Assert.Equal(group, await _client.GetFromJsonAsync<GroupResponse>(path + $"/groups/{group.Id}"));
    }

    [Fact]
    public async Task ResourcePathsAreScopedToBothHouseholdAndCategory()
    {
        var household = await CreateHousehold();
        var other = await CreateHousehold();
        var category = await CreateCategory(household.Id);
        var second = await CreateCategory(household.Id);
        var path = $"{Categories(household.Id)}/{category.Id}";
        var group = await CreateGroup(path);
        foreach (var wrong in new[] { $"{Categories(other.Id)}/{category.Id}", $"{Categories(Guid.NewGuid())}/{category.Id}" })
        {
            await AssertProblem(await _client.GetAsync(wrong), 404);
            await AssertProblem(await _client.PutAsJsonAsync(wrong, new { name = "Changed" }), 404);
            await AssertProblem(await _client.GetAsync(wrong + "/groups"), 404);
            await AssertProblem(await _client.PostAsJsonAsync(wrong + "/groups", new { name = "New" }), 404);
            await AssertProblem(await _client.GetAsync(wrong + $"/groups/{group.Id}"), 404);
            await AssertProblem(await _client.PutAsJsonAsync(wrong + $"/groups/{group.Id}", new { name = "Changed" }), 404);
        }
        var wrongCategory = $"{Categories(household.Id)}/{second.Id}/groups/{group.Id}";
        await AssertProblem(await _client.GetAsync(wrongCategory), 404);
        await AssertProblem(await _client.PutAsJsonAsync(wrongCategory, new { name = "Changed" }), 404);
        Assert.Empty((await _client.GetFromJsonAsync<CategoryResponse[]>(Categories(other.Id)))!);
        Assert.Empty((await _client.GetFromJsonAsync<GroupResponse[]>($"{Categories(household.Id)}/{second.Id}/groups"))!);
        Assert.Equal(category, await _client.GetFromJsonAsync<CategoryResponse>(path));
        Assert.Equal(group, await _client.GetFromJsonAsync<GroupResponse>(path + $"/groups/{group.Id}"));
    }

    [Fact]
    public async Task MissingParentsAndResourcesReturnNotFoundRatherThanEmptyListsOrNewRows()
    {
        var household = await CreateHousehold();
        var absentHousehold = Categories(Guid.NewGuid());
        await AssertProblem(await _client.GetAsync(absentHousehold), 404);
        await AssertProblem(await _client.PostAsJsonAsync(absentHousehold, new { name = "Category", financialType = "Asset" }), 404);
        var missingCategory = $"{Categories(household.Id)}/{Guid.NewGuid()}";
        await AssertProblem(await _client.GetAsync(missingCategory), 404);
        await AssertProblem(await _client.PutAsJsonAsync(missingCategory, new { name = "New" }), 404);
        await AssertProblem(await _client.GetAsync(missingCategory + "/groups"), 404);
        await AssertProblem(await _client.PostAsJsonAsync(missingCategory + "/groups", new { name = "Group" }), 404);
        var category = await CreateCategory(household.Id);
        var missingGroup = $"{Categories(household.Id)}/{category.Id}/groups/{Guid.NewGuid()}";
        await AssertProblem(await _client.GetAsync(missingGroup), 404);
        await AssertProblem(await _client.PutAsJsonAsync(missingGroup, new { name = "New" }), 404);
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.Categories.CountAsync());
        Assert.Empty(await context.ItemGroups.ToArrayAsync());
    }

    [Fact]
    public async Task ListsHaveStableOrderingAndDuplicateNamesKeepDistinctIdentities()
    {
        var household = await CreateHousehold();
        var categories = Categories(household.Id);
        var first = await CreateCategory(household.Id, "Zulu");
        var second = await CreateCategory(household.Id, "Alpha");
        var third = await CreateCategory(household.Id, "Alpha");
        var list = (await _client.GetFromJsonAsync<CategoryResponse[]>(categories))!;
        Assert.Equal(new[] { second.Id, third.Id }.OrderBy(id => id).Append(first.Id), list.Select(row => row.Id));
        var path = $"{categories}/{first.Id}";
        var g1 = await CreateGroup(path, "Zulu");
        var g2 = await CreateGroup(path, "Alpha");
        var g3 = await CreateGroup(path, "Alpha");
        var groups = (await _client.GetFromJsonAsync<GroupResponse[]>(path + "/groups"))!;
        Assert.Equal(new[] { g2.Id, g3.Id }.OrderBy(id => id).Append(g1.Id), groups.Select(row => row.Id));
        Assert.Equal(list, await _client.GetFromJsonAsync<CategoryResponse[]>(categories));
    }

    [Fact]
    public async Task RenameUpdatesCurrentSetupAndOnlySubsequentSnapshotLabels()
    {
        var household = await CreateHousehold();
        var category = await CreateCategory(household.Id);
        var path = $"{Categories(household.Id)}/{category.Id}";
        var group = await CreateGroup(path);
        var item = await new TrackedItemService(_database).CreateAsync(household.Id, group.Id,
            household.People[0].Id, "Reserve", "GBP", "GB", true);
        var snapshots = new SnapshotService(_database);
        var beforeRename = await snapshots.CreateAsync(household.Id, new DateOnly(2023, 1, 1));
        (await _client.PutAsJsonAsync(path, new { name = "New category" })).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync(path + $"/groups/{group.Id}", new { name = "New group" })).EnsureSuccessStatusCode();
        var afterRename = await snapshots.CreateAsync(household.Id, new DateOnly(2023, 2, 1));
        await using var context = _database.CreateDbContext();
        var saved = await context.TrackedItems.Include(row => row.Group).ThenInclude(row => row.Category).SingleAsync();
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal(group.Id, saved.GroupId);
        Assert.Equal("New category", saved.Group.Category.Name);
        Assert.Equal("New group", saved.Group.Name);
        var history = await context.ItemSnapshots.SingleAsync(row => row.SnapshotId == beforeRename.Id);
        Assert.Equal(category.Name, history.CategoryName);
        Assert.Equal(group.Name, history.GroupName);
        var current = await context.ItemSnapshots.SingleAsync(row => row.SnapshotId == afterRename.Id);
        Assert.Equal("New category", current.CategoryName);
        Assert.Equal("New group", current.GroupName);
    }

    private async Task<HouseholdResponse> CreateHousehold()
    {
        var result = await _client.PostAsJsonAsync("/api/households", new { name = "Home", firstPersonDisplayName = "Alex" });
        result.EnsureSuccessStatusCode();
        return (await result.Content.ReadFromJsonAsync<HouseholdResponse>())!;
    }

    private async Task<CategoryResponse> CreateCategory(Guid householdId, string name = "Category")
    {
        var result = await _client.PostAsJsonAsync(Categories(householdId), new { name, financialType = "Asset" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<CategoryResponse>())!;
    }

    private async Task<GroupResponse> CreateGroup(string categoryPath, string name = "Group")
    {
        var result = await _client.PostAsJsonAsync(categoryPath + "/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<GroupResponse>())!;
    }

    private static string Categories(Guid householdId) => $"/api/households/{householdId}/categories";
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static void AssertField(JsonElement problem, string field)
    {
        var messages = problem.GetProperty("errors").GetProperty(field).EnumerateArray().ToArray();
        Assert.NotEmpty(messages);
        Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message.GetString())));
    }

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
