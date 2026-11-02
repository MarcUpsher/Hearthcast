using System.Net;
using System.Net.Http.Json;
using Hearthcast.Api.FinancedAssets.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Api.TrackedItems.Contracts;
using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Api.Tests.Renaming;

public sealed class SetupEditApiTests : IAsyncLifetime
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditsNameAndAssociationsTogetherWhilePreservingHistoricalContext(bool financed)
    {
        var data = await Setup(financed);
        var snapshots = new SnapshotService(_database);
        var original = await snapshots.CreateAsync(data.Home, new DateOnly(2023, 1, 1),
            financed ? [] : [new(data.Id, 100m)], DatePrecision.Month,
            financed ? [new(data.Id, FinancedAssetBalanceKind.CurrentValuation, 300m),
                new(data.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, 200m)] : []);
        await _client.PutAsJsonAsync(data.Path + "/retirement", new { isRetired = true });
        var response = await _client.PutAsJsonAsync(data.Path + "/details", Payload(data, financed, data.Second));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (financed)
        {
            var updated = (await response.Content.ReadFromJsonAsync<FinancedAssetResponse>())!;
            Assert.Equal(data.Id, updated.Id);
            Assert.Equal("Updated", updated.Name);
            Assert.True(updated.IsRetired);
            Assert.Equal(250m, updated.OriginalLoanAmount);
            Assert.Equal(data.Second, Assert.Single(updated.Owners).PersonId);
            Assert.Equal(data.First, Assert.Single(updated.LoanHolderPersonIds));
            Assert.Equal(data.Second, Assert.Single(updated.HouseholdAllocations).PersonId);
            Assert.Equivalent(updated, await _client.GetFromJsonAsync<FinancedAssetResponse>(data.Path));
        }
        else
        {
            var updated = (await response.Content.ReadFromJsonAsync<TrackedItemResponse>())!;
            Assert.Equal(data.Id, updated.Id);
            Assert.Equal("Updated", updated.Name);
            Assert.Equal("Platform", updated.InstitutionName);
            Assert.Null(updated.FundName);
            Assert.True(updated.IsRetired);
            Assert.Equal(data.First, updated.HolderPersonId);
            Assert.Equal(data.Second, Assert.Single(updated.Allocations).PersonId);
            Assert.Equivalent(updated, await _client.GetFromJsonAsync<TrackedItemResponse>(data.Path));
        }
        var saved = await snapshots.GetAsync(data.Home, original.Id);
        Assert.Equal(original.Revision, saved.Revision);
        await _client.PutAsJsonAsync(data.Path + "/retirement", new { isRetired = false });
        var next = await snapshots.CreateNextAsync(data.Home, new DateOnly(2023, 2, 1));
        if (financed)
        {
            Assert.Equal("Original", Assert.Single(saved.FinancedAssetSnapshots).AssetName);
            Assert.Equal(2, Assert.Single(saved.FinancedAssetSnapshots).Owners.Count);
            var updated = Assert.Single(next.FinancedAssetSnapshots);
            Assert.Equal("Updated", updated.AssetName);
            Assert.Equal(100m, updated.CurrentEquity);
            Assert.Single(updated.Owners);
            Assert.Equal(10000, Assert.Single(updated.HouseholdAllocations).ShareBasisPoints);
        }
        else
        {
            Assert.Equal("Original", Assert.Single(saved.ItemSnapshots).ItemName);
            Assert.Equal(2, Assert.Single(saved.ItemSnapshots).HouseholdAllocations.Count);
            var updated = Assert.Single(next.ItemSnapshots);
            Assert.Equal("Updated", updated.ItemName);
            Assert.Equal(100m, updated.Amount);
            Assert.Equal(10000, Assert.Single(updated.HouseholdAllocations).ShareBasisPoints);
        }
    }

    [Theory]
    [InlineData(false, "ineligible", HttpStatusCode.Conflict)]
    [InlineData(true, "ineligible", HttpStatusCode.Conflict)]
    [InlineData(false, "foreign", HttpStatusCode.NotFound)]
    [InlineData(true, "foreign", HttpStatusCode.NotFound)]
    [InlineData(false, "wrong-household", HttpStatusCode.NotFound)]
    [InlineData(true, "wrong-household", HttpStatusCode.NotFound)]
    [InlineData(false, "bad-total", HttpStatusCode.BadRequest)]
    [InlineData(true, "bad-total", HttpStatusCode.BadRequest)]
    public async Task RejectedAssociationsRollBackNameAndAllOtherChanges(bool financed, string scenario, HttpStatusCode expected)
    {
        var data = await Setup(financed);
        var households = new HouseholdService(_database);
        var other = await households.CreateAsync("Other", "Taylor");
        var recipient = scenario == "foreign" ? other.People.Single().Id : data.Second;
        if (scenario == "ineligible")
            recipient = (await households.CreatePersonAsync(data.Home, "Nonparticipant", false)).Id;
        var path = scenario == "wrong-household"
            ? data.Path.Replace(data.Home.ToString(), other.Id.ToString()) : data.Path;
        var before = await _client.GetStringAsync(data.Path);
        var response = await _client.PutAsJsonAsync(path + "/details", Payload(data, financed, recipient, scenario == "bad-total" ? 9999 : 10000));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await _client.GetStringAsync(data.Path));
    }

    private static object Payload(SetupData data, bool financed, Guid recipient, int share = 10000) => financed
        ? new
        {
            name = " Updated ",
            includeInHousehold = true,
            owners = new[] { new { personId = data.Second, shareBasisPoints = 10000 } },
            loanHolderPersonIds = new[] { data.First },
            householdAllocations = new[] { new { personId = recipient, shareBasisPoints = share } }
        }
        : new
        {
            name = " Updated ",
            institutionName = " Platform ",
            fundName = (string?)null,
            includeInHousehold = true,
            allocations = new[] { new { personId = recipient, shareBasisPoints = share } }
        };

    private async Task<SetupData> Setup(bool financed)
    {
        var households = new HouseholdService(_database);
        var home = await households.CreateAsync("Example", "Alex");
        var second = await households.CreatePersonAsync(home.Id, "Morgan", true);
        var first = home.People.Single().Id;
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(home.Id, "Assets", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(home.Id, category.Id, "Holdings");
        var id = financed
            ? (await new FinancedAssetService(_database).CreateAsync(home.Id, group.Id, "Original", "GBP", "GB",
                new DateOnly(2020, 1, 1), 400m, 250m, true, [new(first, 5000), new(second.Id, 5000)], [second.Id],
                true, [new(first, 5000), new(second.Id, 5000)])).Id
            : (await new TrackedItemService(_database).CreateAsync(home.Id, group.Id, first, "Original", "GBP", "GB", true,
                [new(first, 5000), new(second.Id, 5000)])).Id;
        return new(home.Id, first, second.Id, id, $"/api/households/{home.Id}/{(financed ? "financed-assets" : "tracked-items")}/{id}");
    }

    private sealed record SetupData(Guid Home, Guid First, Guid Second, Guid Id, string Path);
}
