using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Hearthcast.Api.Tests.Infrastructure;

namespace Hearthcast.Api.Tests.Snapshots;

public sealed class SnapshotAdditionApiTests : IAsyncLifetime
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
    public async Task AddsOnlyExplicitItemsAndCorrectsValuesAtomicallyWithoutChangingLaterSnapshots()
    {
        var data = await Setup();
        var snapshots = new SnapshotService(_database);
        var later = await snapshots.CreateNextAsync(data.Home, new DateOnly(2023, 2, 1));
        await new TrackedItemService(_database).SetRetiredAsync(data.Home, data.Existing, true);
        var response = await _client.PatchAsJsonAsync(data.Path, new
        {
            expectedRevision = data.Snapshot.Revision,
            values = new[] { new { trackedItemId = data.Existing, amount = 25m } },
            addedItems = new[] { new { trackedItemId = data.Added, amount = 0m } }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<SnapshotResponse>())!;
        Assert.NotEqual(data.Snapshot.Revision, saved.Revision);
        Assert.Equal(2, saved.ItemSnapshots.Count);
        Assert.Equal(25m, saved.ItemSnapshots.Single(i => i.TrackedItemId == data.Existing).Amount);
        var added = saved.ItemSnapshots.Single(i => i.TrackedItemId == data.Added);
        Assert.Equal(0m, added.Amount);
        Assert.Equal("New holding", added.ItemName);
        Assert.Equal("Example platform", added.InstitutionName);
        Assert.Equal(SnapshotValueOrigin.Confirmed, added.ValueOrigin);
        Assert.Equal(data.Snapshot.SnapshotDate, added.LastConfirmedDate);
        var retained = await snapshots.GetAsync(data.Home, later.Id);
        Assert.Equal(later.Revision, retained.Revision);
        Assert.Equal(20m, retained.ItemSnapshots.Single(i => i.TrackedItemId == data.Existing).Amount);
        Assert.Null(retained.ItemSnapshots.Single(i => i.TrackedItemId == data.Added).Amount);
        var persisted = await snapshots.GetAsync(data.Home, data.Snapshot.Id);
        Assert.Equal(saved.Revision, persisted.Revision);
        Assert.Equal(2, persisted.ItemSnapshots.Count);
    }

    [Fact]
    public async Task AdditionCanCopyANewHolderWithoutRewritingTheSavedRoster()
    {
        var data = await Setup();
        var households = new HouseholdService(_database);
        var child = await households.CreatePersonAsync(data.Home, "Child", false);
        var item = await new TrackedItemService(_database).CreateAsync(data.Home, data.Group, child.Id,
            "Child savings", "GBP", "GB", false);
        var response = await _client.PatchAsJsonAsync(data.Path, new
        {
            expectedRevision = data.Snapshot.Revision,
            addedItems = new[] { new { trackedItemId = item.Id, amount = (decimal?)null } }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await new SnapshotService(_database).GetAsync(data.Home, data.Snapshot.Id);
        Assert.Equal(2, saved.People.Count);
        Assert.Equal(data.Snapshot.People.Single().Id, saved.People.Single(p => p.PersonId != child.Id).Id);
        Assert.Equal("Child", saved.People.Single(p => p.PersonId == child.Id).DisplayName);
        Assert.Null(saved.ItemSnapshots.Single(i => i.TrackedItemId == item.Id).Amount);
    }

    [Fact]
    public async Task SharedAdditionCopiesAllocationsAndContributesOnceToHouseholdTotals()
    {
        var data = await Setup();
        var holder = data.Snapshot.People.Single().PersonId;
        var other = await new HouseholdService(_database).CreatePersonAsync(data.Home, "Morgan", true);
        await new TrackedItemService(_database).SetHouseholdAllocationAsync(data.Home, data.Added, true,
            [new(holder, 5000), new(other.Id, 5000)]);
        var response = await _client.PatchAsJsonAsync(data.Path, new
        {
            expectedRevision = data.Snapshot.Revision,
            addedItems = new[] { new { trackedItemId = data.Added, amount = 100m } }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await new SnapshotService(_database).GetAsync(data.Home, data.Snapshot.Id);
        var added = saved.ItemSnapshots.Single(item => item.TrackedItemId == data.Added);
        Assert.True(added.IncludeInHousehold);
        Assert.Equal(2, added.HouseholdAllocations.Count);
        Assert.All(added.HouseholdAllocations, share => Assert.Equal(5000, share.ShareBasisPoints));
        Assert.Equal(new[] { holder, other.Id }.Order(),
            added.HouseholdAllocations.Select(share => share.SnapshotPerson.PersonId).Order());
        using var summary = JsonDocument.Parse(await _client.GetStringAsync(
            data.Path.Replace("/values", "/household-summary")));
        Assert.Equal(120m, summary.RootElement.GetProperty("currencies")[0].GetProperty("totals")
            .GetProperty("knownNetPosition").GetDecimal());
        var personTotals = summary.RootElement.GetProperty("people").EnumerateArray()
            .ToDictionary(person => person.GetProperty("person").GetProperty("personId").GetGuid(),
                person => person.GetProperty("currencies")[0].GetProperty("totals")
                    .GetProperty("knownNetPosition").GetDecimal());
        Assert.Equal(2, personTotals.Count);
        Assert.Equal(70m, personTotals[holder]);
        Assert.Equal(50m, personTotals[other.Id]);
    }

    [Fact]
    public async Task HistoricalNonparticipantCannotReceiveAnAddedAllocationAndNothingIsSaved()
    {
        var data = await Setup();
        var households = new HouseholdService(_database);
        var other = await households.CreatePersonAsync(data.Home, "Morgan", false);
        var snapshot = await new SnapshotService(_database).CreateNextAsync(data.Home, new DateOnly(2023, 2, 1));
        await households.SetPersonParticipationAsync(data.Home, other.Id, true);
        var holder = data.Snapshot.People.Single().PersonId;
        var item = await new TrackedItemService(_database).CreateAsync(data.Home, data.Group, holder,
            "Shared savings", "GBP", "GB", true, [new(holder, 5000), new(other.Id, 5000)]);
        var path = $"/api/households/{data.Home}/snapshots/{snapshot.Id}";
        var before = await _client.GetStringAsync(path);
        var response = await _client.PatchAsJsonAsync(path + "/values", new
        {
            expectedRevision = snapshot.Revision,
            values = new[] { new { trackedItemId = data.Existing, amount = 99m } },
            addedItems = new[] { new { trackedItemId = item.Id, amount = 100m } }
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await _client.GetStringAsync(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidAdditionIdentifiesAddedItemsInValidationErrors(bool duplicate)
    {
        var data = await Setup();
        var item = new { trackedItemId = duplicate ? data.Added : Guid.Empty, amount = 10m };
        var response = await _client.PatchAsJsonAsync(data.Path, new
        {
            expectedRevision = data.Snapshot.Revision,
            addedItems = duplicate ? new[] { item, item } : new[] { item }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("AddedItems", out _));
        Assert.False(errors.TryGetProperty("Values", out _));
    }

    [Theory]
    [InlineData("existing", HttpStatusCode.BadRequest)]
    [InlineData("retired", HttpStatusCode.BadRequest)]
    [InlineData("foreign", HttpStatusCode.BadRequest)]
    [InlineData("stale", HttpStatusCode.Conflict)]
    [InlineData("negative", HttpStatusCode.BadRequest)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("overlap", HttpStatusCode.BadRequest)]
    [InlineData("bad-correction", HttpStatusCode.BadRequest)]
    public async Task RejectedAdditionLeavesRevisionRosterAndValuesUnchanged(string scenario, HttpStatusCode expected)
    {
        var data = await Setup();
        if (scenario == "retired") await new TrackedItemService(_database).SetRetiredAsync(data.Home, data.Added, true);
        var id = scenario == "existing" ? data.Existing : scenario == "foreign" ? Guid.NewGuid() : data.Added;
        var additions = new[] { new { trackedItemId = id, amount = scenario == "negative" ? -1m : 10m } };
        if (scenario == "duplicate") additions = [additions[0], additions[0]];
        var before = await _client.GetStringAsync(data.Path.Replace("/values", ""));
        var response = await _client.PatchAsJsonAsync(data.Path, new
        {
            expectedRevision = scenario == "stale" ? Guid.NewGuid() : data.Snapshot.Revision,
            addedItems = additions,
            values = new[] { new { trackedItemId = scenario == "overlap" ? data.Added : scenario == "bad-correction" ? Guid.NewGuid() : data.Existing, amount = 99m } }
        });
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await _client.GetStringAsync(data.Path.Replace("/values", "")));
    }

    private async Task<Data> Setup()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Savings", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Cash");
        var items = new TrackedItemService(_database);
        var existing = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "Existing", "GBP", "GB", true);
        var snapshot = await new SnapshotService(_database).CreateAsync(home.Id, new DateOnly(2023, 1, 1), [new(existing.Id, 20m)]);
        var added = await items.CreateAsync(home.Id, group.Id, home.People.Single().Id, "New holding", "GBP", "GB", false,
            institutionName: "Example platform");
        return new(home.Id, group.Id, existing.Id, added.Id, snapshot, $"/api/households/{home.Id}/snapshots/{snapshot.Id}/values");
    }
    private sealed record Data(Guid Home, Guid Group, Guid Existing, Guid Added, Snapshot Snapshot, string Path);
}
