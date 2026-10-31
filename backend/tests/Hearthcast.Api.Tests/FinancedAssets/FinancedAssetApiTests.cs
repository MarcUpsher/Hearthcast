using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthcast.Api.FinancedAssets.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.FinancedAssets;

public sealed class FinancedAssetApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new ApiTestFactory(_database);
        _client = _factory.CreateClient(new() { HandleCookies = false });
    }
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }
    [Fact]
    public async Task SetupAndAssociationsPersistAcrossCreateReadAndInventory()
    {
        var s = await Setup();
        Assert.Empty((await _client.GetFromJsonAsync<FinancedAssetResponse[]>(PathFor(s.Home)))!);
        var result = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var asset = (await result.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        Assert.EndsWith($"{PathFor(s.Home)}/{asset.Id}", result.Headers.Location!.ToString());
        Assert.Equal("Home", asset.Name);
        Assert.Equal("GBP", asset.CurrencyCode);
        Assert.Equal("GB", asset.CountryCode);
        Assert.Equal(s.Home, asset.HouseholdId);
        Assert.Equal(s.Group, asset.GroupId);
        Assert.Equal(150000m, asset.PurchasePrice);
        Assert.Equal(100000m, asset.OriginalLoanAmount);
        Assert.Equal(new DateOnly(2020, 1, 2), asset.PurchaseDate);
        Assert.True(asset.HasWithdrawalFacility);
        Assert.True(asset.IncludeInHousehold);
        Assert.False(asset.IsRetired);
        Assert.Equal(2, asset.Owners.Count);
        Assert.All(asset.Owners, share => Assert.Equal(5000, share.ShareBasisPoints));
        Assert.Contains(asset.Owners, share => share.PersonId == s.First);
        Assert.Contains(asset.Owners, share => share.PersonId == s.Second);
        Assert.Equal(s.Second, Assert.Single(asset.LoanHolderPersonIds));
        Assert.Equal(new FinancedAssetShareResponse(s.First, 10000), Assert.Single(asset.HouseholdAllocations));
        using var independent = _factory.CreateClient(new() { HandleCookies = false });
        Assert.Equivalent(asset, await independent.GetFromJsonAsync<FinancedAssetResponse>(result.Headers.Location));
        Assert.Equivalent(asset, Assert.Single((await independent.GetFromJsonAsync<FinancedAssetResponse[]>(PathFor(s.Home)))!));
        await using var context = _database.CreateDbContext();
        Assert.Equal(2, await context.FinancedAssetOwners.CountAsync());
        Assert.Equal(1, await context.FinancedAssetLoanHolders.CountAsync());
        Assert.Equal(1, await context.FinancedAssetHouseholdAllocations.CountAsync());
    }
    [Theory]
    [InlineData("groupId", "GroupId")]
    [InlineData("name", "Name")]
    [InlineData("currencyCode", "CurrencyCode")]
    [InlineData("countryCode", "CountryCode")]
    [InlineData("originalLoanAmount", "OriginalLoanAmount")]
    [InlineData("hasWithdrawalFacility", "HasWithdrawalFacility")]
    [InlineData("includeInHousehold", "IncludeInHousehold")]
    [InlineData("owners", "Owners")]
    [InlineData("loanHolderPersonIds", "LoanHolderPersonIds")]
    [InlineData("householdAllocations", "HouseholdAllocations")]
    public async Task RequiredFieldsRejectOmissionAndNullIndependently(string field, string errorField)
    {
        var s = await Setup();
        foreach (var omitted in new[] { true, false })
        {
            var body = Body(s);
            if (omitted)
                body.Remove(field);
            else
                body[field] = null;
            var problem = await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 400);
            Assert.NotEmpty(problem.GetProperty("errors").GetProperty(errorField).EnumerateArray());
        }
        await AssertNoAssets();
    }
    [Theory]
    [InlineData("owners", "[]")]
    [InlineData("owners", "[null]")]
    [InlineData("owners", "[{}]")]
    [InlineData("householdAllocations", "[]")]
    [InlineData("householdAllocations", "[null]")]
    [InlineData("originalLoanAmount", "-1")]
    [InlineData("purchasePrice", "-1")]
    [InlineData("name", "\" \"")]
    [InlineData("name", "\"Bad\\u0000name\"")]
    [InlineData("currencyCode", "\"G1P\"")]
    [InlineData("countryCode", "\"GBR\"")]
    [InlineData("hasWithdrawalFacility", "\"false\"")]
    [InlineData("purchaseDate", "\"not-a-date\"")]
    [InlineData("outstandingBalance", "123")]
    public async Task InvalidSetupDoesNotLeavePartialRows(string field, string value)
    {
        var s = await Setup();
        var body = Body(s);
        body[field] = JsonNode.Parse(value);
        await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 400);
        await AssertNoAssets();
    }
    [Theory]
    [InlineData("groupId")]
    [InlineData("owners")]
    [InlineData("loanHolderPersonIds")]
    [InlineData("householdAllocations")]
    public async Task CrossHouseholdReferencesAreRejected(string field)
    {
        var s = await Setup();
        var other = await Setup();
        var body = Body(s);
        body[field] = field switch {
            "groupId" => JsonValue.Create(other.Group),
            "loanHolderPersonIds" => JsonSerializer.SerializeToNode(new[] { other.First }),
            _ => Shares(other.First) };
        await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 404);
        await AssertNoAssets();
    }
    [Fact]
    public async Task NonparticipantsCanOwnAndHoldLoansButCannotReceiveHouseholdShares()
    {
        var s = await Setup();
        await new HouseholdService(_database).SetPersonParticipationAsync(s.Home, s.Second, false);
        var body = Body(s);
        body["householdAllocations"] = Shares(s.Second);
        await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 409);
        await AssertNoAssets();
        var created = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }
    [Fact]
    public async Task ExcludedAssetCanHaveNoLoanOrKnownPurchaseDetails()
    {
        var s = await Setup();
        var body = Body(s);
        body.Remove("purchaseDate");
        body.Remove("purchasePrice");
        body["originalLoanAmount"] = 0;
        body["hasWithdrawalFacility"] = false;
        body["includeInHousehold"] = false;
        body["householdAllocations"] = new JsonArray();
        body["loanHolderPersonIds"] = new JsonArray();
        var created = await _client.PostAsJsonAsync(PathFor(s.Home), body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var asset = (await created.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        Assert.Null(asset.PurchaseDate);
        Assert.Null(asset.PurchasePrice);
        Assert.Empty(asset.HouseholdAllocations);
        Assert.Empty(asset.LoanHolderPersonIds);
        Assert.False(asset.HasWithdrawalFacility);
        Assert.False(asset.IncludeInHousehold);
    }
    [Fact]
    public async Task InventoryIncludesRetiredAndExcludedAssetsInStableNameOrder()
    {
        var setup = await Setup();
        var created = new List<FinancedAssetResponse>();
        foreach (var name in new[] { "Zulu", "Beta", "Alpha", "Beta" })
        {
            var body = Body(setup);
            body["name"] = name;
            if (name == "Alpha")
            {
                body["includeInHousehold"] = false;
                body["householdAllocations"] = new JsonArray();
            }

            var response = await _client.PostAsJsonAsync(PathFor(setup.Home), body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created.Add((await response.Content.ReadFromJsonAsync<FinancedAssetResponse>())!);
        }

        await new Hearthcast.Data.FinancedAssets.FinancedAssetService(_database)
            .SetRetiredAsync(setup.Home, created[0].Id, true);

        var inventory = (await _client.GetFromJsonAsync<FinancedAssetResponse[]>(PathFor(setup.Home)))!;
        Assert.Equal(4, inventory.Length);
        Assert.Equal(created.OrderBy(asset => asset.Name).ThenBy(asset => asset.Id).Select(asset => asset.Id),
            inventory.Select(asset => asset.Id));
        Assert.False(inventory.Single(asset => asset.Name == "Alpha").IncludeInHousehold);
        Assert.True(inventory.Single(asset => asset.Id == created[0].Id).IsRetired);
    }

    [Fact]
    public async Task DebtCategoryGroupIsRejected()
    {
        var s = await Setup();
        var classification = new ClassificationService(_database);
        var debt = await classification.CreateCategoryAsync(s.Home, "Debt", FinancialType.Debt);
        var group = await classification.CreateGroupAsync(s.Home, debt.Id, "Loans");
        var body = Body(s);
        body["groupId"] = JsonValue.Create(group.Id);
        await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 400);
        await AssertNoAssets();
    }

    [Theory]
    [InlineData("owners", "duplicate")]
    [InlineData("owners", "incomplete")]
    [InlineData("owners", "zero")]
    [InlineData("owners", "emptyPerson")]
    [InlineData("householdAllocations", "duplicate")]
    [InlineData("householdAllocations", "incomplete")]
    [InlineData("householdAllocations", "zero")]
    [InlineData("householdAllocations", "emptyPerson")]
    public async Task InvalidSharesAreRejected(string field, string problem)
    {
        var s = await Setup();
        var shares = problem switch
        {
            "duplicate" => JsonSerializer.SerializeToNode(new[] {
                new { personId = s.First, shareBasisPoints = 5000 },
                new { personId = s.First, shareBasisPoints = 5000 }
            })!,
            "incomplete" => Shares(s.First, 9999),
            "zero" => Shares(s.First, 0),
            "emptyPerson" => Shares(Guid.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(problem))
        };
        var body = Body(s);
        body[field] = shares;
        await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 400);
        await AssertNoAssets();
    }

    [Fact]
    public async Task ExcludedAssetCannotHaveHouseholdAllocations()
    {
        var s = await Setup();
        var body = Body(s);
        body["includeInHousehold"] = false;
        await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 400);
        await AssertNoAssets();
    }

    [Fact]
    public async Task DuplicateLoanHoldersAreRejected()
    {
        var s = await Setup();
        var body = Body(s);
        body["loanHolderPersonIds"] = JsonSerializer.SerializeToNode(new[] { s.First, s.First });
        await Problem(await _client.PostAsJsonAsync(PathFor(s.Home), body), 400);
        await AssertNoAssets();
    }
    [Fact]
    public async Task ReadsAndInventoryRespectScopeAndMissingParents()
    {
        var s = await Setup();
        var other = await Setup();
        var result = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        result.EnsureSuccessStatusCode();
        var asset = (await result.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        Assert.Empty((await _client.GetFromJsonAsync<FinancedAssetResponse[]>(PathFor(other.Home)))!);
        await Problem(await _client.GetAsync($"{PathFor(other.Home)}/{asset.Id}"), 404);
        await Problem(await _client.GetAsync($"{PathFor(s.Home)}/{Guid.NewGuid()}"), 404);
        await Problem(await _client.GetAsync(PathFor(Guid.NewGuid())), 404);
        await Problem(await _client.PostAsJsonAsync(PathFor(Guid.NewGuid()), Body(s)), 404);
    }

    [Fact]
    public async Task AssociationReplacementChangesFutureSnapshotsAndPreservesEarlierOnes()
    {
        var s = await Setup();
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        var path = $"{PathFor(s.Home)}/{original.Id}/associations";
        var snapshots = new Hearthcast.Data.Snapshots.SnapshotService(_database);
        var first = await snapshots.CreateAsync(s.Home, new DateOnly(2023, 1, 1));

        var replacement = AssociationBody(s);
        replacement["owners"] = Shares(s.Second);
        replacement["loanHolderPersonIds"] = JsonSerializer.SerializeToNode(new[] { s.First });
        replacement["householdAllocations"] = Shares(s.Second);
        var response = await _client.PutAsJsonAsync(path, replacement);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var changed = (await response.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        Assert.Equal(original.Id, changed.Id);
        Assert.Equal(new FinancedAssetShareResponse(s.Second, 10_000), Assert.Single(changed.Owners));
        Assert.Equal(s.First, Assert.Single(changed.LoanHolderPersonIds));
        Assert.Equal(new FinancedAssetShareResponse(s.Second, 10_000), Assert.Single(changed.HouseholdAllocations));
        Assert.Equivalent(changed, await _client.GetFromJsonAsync<FinancedAssetResponse>($"{PathFor(s.Home)}/{original.Id}"));
        Assert.Equivalent(changed, (await (await _client.PutAsJsonAsync(path, replacement))
            .Content.ReadFromJsonAsync<FinancedAssetResponse>())!);

        var second = await snapshots.CreateAsync(s.Home, new DateOnly(2023, 2, 1));
        var historical = Assert.Single((await snapshots.GetAsync(s.Home, first.Id)).FinancedAssetSnapshots);
        Assert.Equal(2, historical.Owners.Count);
        Assert.Equal(s.Second, Assert.Single(historical.LoanHolders).SnapshotPerson.PersonId);
        Assert.Equal(s.First, Assert.Single(historical.HouseholdAllocations).SnapshotPerson.PersonId);
        var updated = Assert.Single((await snapshots.GetAsync(s.Home, second.Id)).FinancedAssetSnapshots);
        Assert.Equal(s.Second, Assert.Single(updated.Owners).SnapshotPerson.PersonId);
        Assert.Equal(s.First, Assert.Single(updated.LoanHolders).SnapshotPerson.PersonId);
        Assert.Equal(s.Second, Assert.Single(updated.HouseholdAllocations).SnapshotPerson.PersonId);

        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.FinancedAssetOwners.CountAsync());
        Assert.Equal(1, await context.FinancedAssetLoanHolders.CountAsync());
        Assert.Equal(1, await context.FinancedAssetHouseholdAllocations.CountAsync());
    }

    [Fact]
    public async Task ExclusionAndRetirementAreReversibleWithoutLosingAssociations()
    {
        var s = await Setup();
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        var path = $"{PathFor(s.Home)}/{original.Id}";
        var excludedBody = AssociationBody(s);
        excludedBody["includeInHousehold"] = false;
        excludedBody["householdAllocations"] = new JsonArray();
        var excludedResponse = await _client.PutAsJsonAsync(path + "/associations", excludedBody);
        Assert.Equal(HttpStatusCode.OK, excludedResponse.StatusCode);
        var excluded = (await excludedResponse.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        Assert.False(excluded.IncludeInHousehold);
        Assert.Empty(excluded.HouseholdAllocations);
        Assert.Equal(original.Owners, excluded.Owners);

        var retirement = await _client.PutAsJsonAsync(path + "/retirement", new { isRetired = true });
        Assert.Equal(HttpStatusCode.OK, retirement.StatusCode);
        var retired = (await retirement.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        Assert.True(retired.IsRetired);
        Assert.Equal(excluded.Owners, retired.Owners);
        Assert.Equal(retired.Id, Assert.Single((await _client.GetFromJsonAsync<FinancedAssetResponse[]>(PathFor(s.Home)))!).Id);
        var snapshots = new Hearthcast.Data.Snapshots.SnapshotService(_database);
        var absent = await snapshots.CreateAsync(s.Home, new DateOnly(2023, 3, 1));
        Assert.Empty(absent.FinancedAssetSnapshots);

        var activation = await _client.PutAsJsonAsync(path + "/retirement", new { isRetired = false });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        Assert.False((await activation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!.IsRetired);
        var restored = await _client.PutAsJsonAsync(path + "/associations", AssociationBody(s));
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        Assert.Equivalent(original, await restored.Content.ReadFromJsonAsync<FinancedAssetResponse>());
    }

    [Fact]
    public async Task RetirementAndReactivationPreserveCustomAssociationsInReadsAndFutureSnapshots()
    {
        var s = await Setup();
        var body = Body(s);
        body["householdAllocations"] = JsonSerializer.SerializeToNode(new[]
        {
            new { personId = s.First, shareBasisPoints = 2500 },
            new { personId = s.Second, shareBasisPoints = 7500 }
        });
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), body);
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        var path = $"{PathFor(s.Home)}/{original.Id}";

        var retirement = await _client.PutAsJsonAsync(path + "/retirement", new { isRetired = true });
        Assert.Equal(HttpStatusCode.OK, retirement.StatusCode);
        Assert.Equivalent(original with { IsRetired = true },
            await retirement.Content.ReadFromJsonAsync<FinancedAssetResponse>());
        Assert.Equivalent(original with { IsRetired = true },
            await _client.GetFromJsonAsync<FinancedAssetResponse>(path));

        var activation = await _client.PutAsJsonAsync(path + "/retirement", new { isRetired = false });
        Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
        Assert.Equivalent(original, await activation.Content.ReadFromJsonAsync<FinancedAssetResponse>());
        Assert.Equivalent(original, await _client.GetFromJsonAsync<FinancedAssetResponse>(path));

        var snapshots = new Hearthcast.Data.Snapshots.SnapshotService(_database);
        var capture = await snapshots.CreateAsync(s.Home, new DateOnly(2023, 4, 1));
        var saved = Assert.Single((await snapshots.GetAsync(s.Home, capture.Id)).FinancedAssetSnapshots);
        Assert.Equal(original.Id, saved.FinancedAssetId);
        Assert.True(saved.IncludeInHousehold);
        Assert.Equal(original.Owners.OrderBy(share => share.PersonId), saved.Owners
            .Select(share => new FinancedAssetShareResponse(share.SnapshotPerson.PersonId, share.ShareBasisPoints))
            .OrderBy(share => share.PersonId));
        Assert.Equal(original.LoanHolderPersonIds, saved.LoanHolders
            .Select(holder => holder.SnapshotPerson.PersonId).OrderBy(id => id));
        Assert.Equal(original.HouseholdAllocations.OrderBy(share => share.PersonId), saved.HouseholdAllocations
            .Select(share => new FinancedAssetShareResponse(share.SnapshotPerson.PersonId, share.ShareBasisPoints))
            .OrderBy(share => share.PersonId));
    }

    [Theory]
    [InlineData("owners", "Owners")]
    [InlineData("loanHolderPersonIds", "LoanHolderPersonIds")]
    [InlineData("includeInHousehold", "IncludeInHousehold")]
    [InlineData("householdAllocations", "HouseholdAllocations")]
    public async Task ReplacementRequiresEveryFieldWhenOmittedOrNull(string field, string errorField)
    {
        var s = await Setup();
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        var path = $"{PathFor(s.Home)}/{original.Id}";

        foreach (var omitted in new[] { true, false })
        {
            var body = AssociationBody(s);
            if (omitted)
                body.Remove(field);
            else
                body[field] = null;

            var problem = await Problem(await _client.PutAsJsonAsync(path + "/associations", body), 400);
            Assert.NotEmpty(problem.GetProperty("errors").GetProperty(errorField).EnumerateArray());
            Assert.Equivalent(original, await _client.GetFromJsonAsync<FinancedAssetResponse>(path));
        }
    }

    [Fact]
    public async Task AssociationReplacementRejectsUnrelatedMetadataWithoutChangingState()
    {
        var s = await Setup();
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        var path = $"{PathFor(s.Home)}/{original.Id}";
        var body = AssociationBody(s);
        body["originalLoanAmount"] = 1;
        // Include a valid association change so the rejected request also proves atomicity.
        body["owners"] = Shares(s.Second);

        await Problem(await _client.PutAsJsonAsync(path + "/associations", body), 400);
        Assert.Equivalent(original, await _client.GetFromJsonAsync<FinancedAssetResponse>(path));
    }

    [Theory]
    [InlineData("owners", "[]", "Owners")]
    [InlineData("loanHolderPersonIds", "null", "LoanHolderPersonIds")]
    [InlineData("householdAllocations", "[]", "HouseholdAllocations")]
    [InlineData("includeInHousehold", "null", "IncludeInHousehold")]
    [InlineData("owners", "[null]", "Owners")]
    public async Task InvalidAssociationReplacementPreservesExistingState(string field, string value, string errorField)
    {
        var s = await Setup();
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        var body = AssociationBody(s);
        body[field] = JsonNode.Parse(value);
        var problem = await Problem(await _client.PutAsJsonAsync($"{PathFor(s.Home)}/{original.Id}/associations", body), 400);
        Assert.True(problem.GetProperty("errors").TryGetProperty(errorField, out _));
        Assert.Equivalent(original, await _client.GetFromJsonAsync<FinancedAssetResponse>($"{PathFor(s.Home)}/{original.Id}"));
    }

    [Fact]
    public async Task AssociationAndRetirementUpdatesRespectHouseholdScopeAndEligibility()
    {
        var s = await Setup();
        var other = await Setup();
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        var path = $"{PathFor(s.Home)}/{original.Id}";
        var foreign = AssociationBody(s);
        foreign["owners"] = Shares(other.First);
        await Problem(await _client.PutAsJsonAsync(path + "/associations", foreign), 404);
        await Problem(await _client.PutAsJsonAsync($"{PathFor(other.Home)}/{original.Id}/associations", AssociationBody(s)), 404);
        await Problem(await _client.PutAsJsonAsync($"{PathFor(other.Home)}/{original.Id}/retirement", new { isRetired = true }), 404);
        await new HouseholdService(_database).SetPersonParticipationAsync(s.Home, s.Second, false);
        var ineligible = AssociationBody(s);
        ineligible["householdAllocations"] = Shares(s.Second);
        await Problem(await _client.PutAsJsonAsync(path + "/associations", ineligible), 409);
        Assert.Equivalent(original, await _client.GetFromJsonAsync<FinancedAssetResponse>(path));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isRetired\":null}")]
    [InlineData("{\"isRetired\":\"true\"}")]
    [InlineData("{\"isRetired\":true,\"name\":\"Changed\"}")]
    public async Task InvalidRetirementRequestDoesNotChangeAsset(string json)
    {
        var s = await Setup();
        var creation = await _client.PostAsJsonAsync(PathFor(s.Home), Body(s));
        creation.EnsureSuccessStatusCode();
        var original = (await creation.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
        await Problem(await _client.PutAsJsonAsync($"{PathFor(s.Home)}/{original.Id}/retirement", JsonNode.Parse(json)), 400);
        Assert.Equivalent(original, await _client.GetFromJsonAsync<FinancedAssetResponse>($"{PathFor(s.Home)}/{original.Id}"));
    }

    private static JsonObject AssociationBody((Guid Home, Guid Group, Guid First, Guid Second) s)
    {
        var body = Body(s);
        foreach (var field in new[] { "groupId", "name", "currencyCode", "countryCode", "purchaseDate",
                     "purchasePrice", "originalLoanAmount", "hasWithdrawalFacility" })
            body.Remove(field);
        return body;
    }

    private async Task<(Guid Home, Guid Group, Guid First, Guid Second)> Setup()
    {
        var households = new HouseholdService(_database);
        var home = await households.CreateAsync("Home", "Alex");
        var first = home.People.Single().Id;
        var second = await households.CreatePersonAsync(home.Id, "Morgan", true);
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Assets", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Property");
        return (home.Id, group.Id, first, second.Id);
    }
    private static JsonObject Body((Guid Home, Guid Group, Guid First, Guid Second) s) => JsonSerializer.SerializeToNode(new {
        groupId = s.Group, name = " Home ", currencyCode = "gbp", countryCode = "gb",
        purchaseDate = "2020-01-02", purchasePrice = 150000m, originalLoanAmount = 100000m,
        hasWithdrawalFacility = true, includeInHousehold = true,
        owners = new[] { new { personId = s.First, shareBasisPoints = 5000 }, new { personId = s.Second, shareBasisPoints = 5000 } },
        loanHolderPersonIds = new[] { s.Second }, householdAllocations = new[] { new { personId = s.First, shareBasisPoints = 10000 } }
    })!.AsObject();
    private static JsonNode Shares(Guid person, int amount = 10000) => JsonSerializer.SerializeToNode(new[] { new { personId = person, shareBasisPoints = amount } })!;
    private static string PathFor(Guid home) => $"/api/households/{home}/financed-assets";
    private async Task AssertNoAssets()
    {
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.FinancedAssets.ToArrayAsync());
        Assert.Empty(await context.FinancedAssetOwners.ToArrayAsync());
        Assert.Empty(await context.FinancedAssetLoanHolders.ToArrayAsync());
        Assert.Empty(await context.FinancedAssetHouseholdAllocations.ToArrayAsync());
    }
    private static async Task<JsonElement> Problem(HttpResponseMessage response, int status)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, json.GetProperty("status").GetInt32());
        Assert.True(json.TryGetProperty("traceId", out _));
        return json;
    }
}
