using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hearthcast.Api.Households.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.Households;

public sealed class HouseholdApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new ApiTestFactory(_database);
        _client = _factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task ListsHouseholdsIncludingPeopleWithoutRequiringAnId()
    {
        Assert.Empty((await _client.GetFromJsonAsync<HouseholdResponse[]>("/api/households"))!);
        await _client.PostAsJsonAsync("/api/households",new { name="Cedar",firstPersonDisplayName="Alex" });
        await _client.PostAsJsonAsync("/api/households",new { name="Birch",firstPersonDisplayName="Morgan" });
        var rows=(await _client.GetFromJsonAsync<HouseholdResponse[]>("/api/households"))!;
        Assert.Equal(new[] { "Birch","Cedar" },rows.Select(r=>r.Name));
        Assert.All(rows,r=>Assert.Single(r.People));
    }

    [Fact]
    public async Task CreateReadRenameAndPersonLifecycleUseRestStatusesAndPersistAcrossClients()
    {
        var response = await _client.PostAsJsonAsync("/api/households", new { name = "  Home  ", firstPersonDisplayName = "  Alex  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var household = (await response.Content.ReadFromJsonAsync<HouseholdResponse>())!;
        Assert.Equal("Home", household.Name);
        Assert.Equal("Alex", Assert.Single(household.People).DisplayName);
        Assert.True(household.People[0].ParticipatesInHouseholdFinances);
        Assert.EndsWith($"/api/households/{household.Id}", response.Headers.Location!.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var independentClient = _factory.CreateClient(new() { HandleCookies = false });
        var loaded = await independentClient.GetFromJsonAsync<HouseholdResponse>(response.Headers.Location);
        Assert.Equal(household.Id, loaded!.Id);
        var renamed = await _client.PutAsJsonAsync($"/api/households/{household.Id}", new { name = "  New home  " });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("New home", (await renamed.Content.ReadFromJsonAsync<HouseholdResponse>())!.Name);
        // Duplicate labels must not be used to identify the newly created person.
        var personResponse = await _client.PostAsJsonAsync($"/api/households/{household.Id}/people",
            new { displayName = "Alex", participatesInHouseholdFinances = false });
        Assert.Equal(HttpStatusCode.Created, personResponse.StatusCode);
        var person = (await personResponse.Content.ReadFromJsonAsync<PersonResponse>())!;
        Assert.NotEqual(household.People[0].Id, person.Id);
        Assert.False(person.ParticipatesInHouseholdFinances);
        Assert.EndsWith($"/api/households/{household.Id}/people/{person.Id}", personResponse.Headers.Location!.ToString());
        Assert.Equal(person, await independentClient.GetFromJsonAsync<PersonResponse>(personResponse.Headers.Location));
        var update = await _client.PutAsJsonAsync(personResponse.Headers.Location,
            new { displayName = "  Morgan  ", participatesInHouseholdFinances = true });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = (await update.Content.ReadFromJsonAsync<PersonResponse>())!;
        Assert.Equal("Morgan", updated.DisplayName);
        Assert.True(updated.ParticipatesInHouseholdFinances);
        var deletion = await _client.DeleteAsync(personResponse.Headers.Location);
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        Assert.Empty(await deletion.Content.ReadAsStringAsync());
        await AssertProblem(await _client.GetAsync(personResponse.Headers.Location), 404);
        await AssertProblem(await _client.DeleteAsync(personResponse.Headers.Location), 404);
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.People.CountAsync());
        Assert.Equal("New home", (await context.Households.SingleAsync()).Name);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{\"name\":null,\"firstPersonDisplayName\":\"Alex\"}")]
    [InlineData("{\"name\":\"  \",\"firstPersonDisplayName\":\"Alex\"}")]
    [InlineData("{\"name\":\"Home\",\"firstPersonDisplayName\":\"\\t\"}")]
    [InlineData("{\"name\":42,\"firstPersonDisplayName\":\"Alex\"}")]
    [InlineData("{\"name\":\"Home\\u0000name\",\"firstPersonDisplayName\":\"Alex\"}")]
    [InlineData("{\"name\":\"Home\",\"firstPersonDisplayName\":\"Alex\\u0000name\"}")]
    public async Task InvalidHouseholdBodiesReturnProblemDetailsAndDoNotWrite(string body)
    {
        var result = await _client.PostAsync("/api/households", Json(body));
        await AssertProblem(result, 400);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.Households.ToArrayAsync());
        Assert.Empty(await context.People.ToArrayAsync());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"displayName\":\"Morgan\"}")]
    [InlineData("{\"displayName\":\"Morgan\",\"participatesInHouseholdFinances\":null}")]
    [InlineData("{\"displayName\":\"Morgan\",\"participatesInHouseholdFinances\":\"false\"}")]
    [InlineData("{\"displayName\":\" \",\"participatesInHouseholdFinances\":false}")]
    public async Task PersonCreateAndUpdateRequireBothFieldsWithoutDefaultingParticipation(string body)
    {
        var household = await CreateHousehold();
        await AssertProblem(await _client.PostAsync($"/api/households/{household.Id}/people", Json(body)), 400);
        await AssertProblem(await _client.PutAsync($"/api/households/{household.Id}/people/{household.People[0].Id}", Json(body)), 400);
        var saved = await _client.GetFromJsonAsync<HouseholdResponse>($"/api/households/{household.Id}");
        Assert.Equal(household.People[0], Assert.Single(saved!.People));
    }

    [Fact]
    public async Task LastParticipantConflictRollsBackTheWholePersonUpdate()
    {
        var household = await CreateHousehold();
        var path = $"/api/households/{household.Id}/people/{household.People[0].Id}";
        await AssertProblem(await _client.PutAsJsonAsync(path,
            new { displayName = "Changed name", participatesInHouseholdFinances = false }), 409);
        var saved = await _client.GetFromJsonAsync<PersonResponse>(path);
        Assert.Equal(household.People[0], saved);
        await AssertProblem(await _client.DeleteAsync(path), 409);
        Assert.Equal(household.People[0], await _client.GetFromJsonAsync<PersonResponse>(path));
    }

    [Fact]
    public async Task NullCharactersInSetupEditsAreRejectedWithoutChangingSavedData()
    {
        var household = await CreateHousehold();
        var path = $"/api/households/{household.Id}";
        await AssertProblem(await _client.PutAsJsonAsync(path, new { name = "Home\0name" }), 400);
        await AssertProblem(await _client.PostAsJsonAsync(path + "/people",
            new { displayName = "Morgan\0name", participatesInHouseholdFinances = false }), 400);
        await AssertProblem(await _client.PutAsJsonAsync(path + $"/people/{household.People[0].Id}",
            new { displayName = "Alex\0name", participatesInHouseholdFinances = false }), 400);
        var saved = (await _client.GetFromJsonAsync<HouseholdResponse>(path))!;
        Assert.Equal(household.Name, saved.Name);
        Assert.Equal(household.People[0], Assert.Single(saved.People));
    }

    [Fact]
    public async Task MissingFieldsAreIdentifiedInValidationErrorsForEverySetupRequest()
    {
        var creation = await AssertProblem(await _client.PostAsync("/api/households", Json("{}")), 400);
        AssertValidationFields(creation, "Name", "FirstPersonDisplayName");
        var household = await CreateHousehold();
        var path = $"/api/households/{household.Id}";
        AssertValidationFields(await AssertProblem(await _client.PutAsync(path, Json("{}")), 400), "Name");
        AssertValidationFields(await AssertProblem(await _client.PostAsync(path + "/people", Json("{}")), 400),
            "DisplayName", "ParticipatesInHouseholdFinances");
        AssertValidationFields(await AssertProblem(await _client.PutAsync(path + $"/people/{household.People[0].Id}", Json("{}")), 400),
            "DisplayName", "ParticipatesInHouseholdFinances");
    }

    [Fact]
    public async Task RequestsCannotReadUpdateOrDeleteAnotherHouseholdsPerson()
    {
        var household = await CreateHousehold();
        var other = await CreateHousehold();
        var path = $"/api/households/{household.Id}/people/{other.People[0].Id}";
        await AssertProblem(await _client.GetAsync(path), 404);
        await AssertProblem(await _client.PutAsJsonAsync(path,
            new { displayName = "Changed", participatesInHouseholdFinances = true }), 404);
        await AssertProblem(await _client.DeleteAsync(path), 404);
        Assert.Equal(other.People[0], await _client.GetFromJsonAsync<PersonResponse>($"/api/households/{other.Id}/people/{other.People[0].Id}"));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("rename")]
    [InlineData("addPerson")]
    public async Task MissingHouseholdsReturnNotFound(string operation)
    {
        var path = $"/api/households/{Guid.NewGuid()}";
        var response = operation switch
        {
            "get" => await _client.GetAsync(path),
            "rename" => await _client.PutAsJsonAsync(path, new { name = "Home" }),
            _ => await _client.PostAsJsonAsync(path + "/people", new { displayName = "Person", participatesInHouseholdFinances = false })
        };
        await AssertProblem(response, 404);
    }

    [Fact]
    public async Task ExistingHoldingsPreventRemovalAndParticipationChangesThroughHttp()
    {
        var household = await CreateHousehold();
        var create = await _client.PostAsJsonAsync($"/api/households/{household.Id}/people",
            new { displayName = "Morgan", participatesInHouseholdFinances = true });
        var person = (await create.Content.ReadFromJsonAsync<PersonResponse>())!;
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(household.Id, "Savings", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(household.Id, category.Id, "Cash");
        await new TrackedItemService(_database).CreateAsync(household.Id, group.Id, person.Id, "Reserve", "GBP", "GB", true);
        var path = create.Headers.Location!;
        await AssertProblem(await _client.DeleteAsync(path), 409);
        await AssertProblem(await _client.PutAsJsonAsync(path,
            new { displayName = "Changed", participatesInHouseholdFinances = false }), 409);
        Assert.Equal(person, await _client.GetFromJsonAsync<PersonResponse>(path));
    }

    [Fact]
    public async Task SnapshotHistoryPreventsRemovingAnOtherwiseUnreferencedNonparticipant()
    {
        var household = await CreateHousehold();
        var create = await _client.PostAsJsonAsync($"/api/households/{household.Id}/people",
            new { displayName = "Morgan", participatesInHouseholdFinances = false });
        await new SnapshotService(_database).CreateAsync(household.Id, new DateOnly(2023, 1, 1));
        var problem = await AssertProblem(await _client.DeleteAsync(create.Headers.Location), 409);
        Assert.Contains("snapshot history", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task JsonContentTypesRoutesAndReadOnlyMethodsHavePredictableErrors()
    {
        var household = await CreateHousehold();
        var path = $"/api/households/{household.Id}";
        await AssertProblem(await _client.PutAsync(path, Json("{\"name\":\" \"}")), 400);
        await AssertProblem(await _client.PostAsync("/api/households", new StringContent("name=Home")), 415);
        await AssertProblem(await _client.GetAsync("/api/households/not-a-guid"), 404);
        await AssertProblem(await _client.GetAsync("/unknown"), 404);
        await AssertProblem(await _client.DeleteAsync(path), 405);
        var saved = await _client.GetFromJsonAsync<HouseholdResponse>(path);
        Assert.Equal(household.Name, saved!.Name);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain")]
    public async Task UnexpectedFailuresReturnGenericServerErrorsRatherThanDomainConflicts(string accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/_tests/errors");
        request.Headers.Accept.ParseAdd(accept);
        var result = await _client.SendAsync(request);
        var problem = await AssertProblem(result, 500);
        Assert.DoesNotContain("Synthetic private", problem.GetRawText());
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.False(problem.TryGetProperty("stackTrace", out _));
    }

    [Theory]
    [InlineData("argument")]
    [InlineData("key")]
    public async Task ProgrammingExceptionsAreServerErrorsAndDoNotExposeTheirMessages(string kind)
    {
        var problem = await AssertProblem(await _client.GetAsync($"/_tests/errors/{kind}"), 500);
        Assert.DoesNotContain("Synthetic private", problem.GetRawText());
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.False(problem.TryGetProperty("stackTrace", out _));
    }

    private static void AssertValidationFields(JsonElement problem, params string[] fields)
    {
        var errors = problem.GetProperty("errors");
        foreach (var field in fields)
        {
            var messages = errors.GetProperty(field).EnumerateArray().ToArray();
            Assert.NotEmpty(messages);
            Assert.All(messages, message => Assert.False(string.IsNullOrWhiteSpace(message.GetString())));
        }
    }

    private async Task<HouseholdResponse> CreateHousehold()
    {
        var response = await _client.PostAsJsonAsync("/api/households", new { name = "Home", firstPersonDisplayName = "Alex" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HouseholdResponse>())!;
    }

    private static StringContent Json(string value) => new(value, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> AssertProblem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
        Assert.True(problem.TryGetProperty("traceId", out _));
        return problem;
    }
}
