using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Snapshots.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;

namespace Hearthcast.Api.Tests.Workflows;

public sealed class HouseholdWorkflowApiTests : IAsyncLifetime
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
    public async Task SetupCaptureReportsAndLaterChangesPreserveIndependentHistory()
    {
        // All application data is created and changed through public HTTP endpoints.
        var home = await PostAsync("/api/households", new { name = "Home", firstPersonDisplayName = "Alex" });
        var path = $"/api/households/{Id(home)}";
        var alex = home.GetProperty("people")[0].GetProperty("id").GetGuid();
        var morgan = Id(await PostAsync(path + "/people", new { displayName = "Morgan", participatesInHouseholdFinances = true }));
        var child = Id(await PostAsync(path + "/people", new { displayName = "Child", participatesInHouseholdFinances = false }));
        var assets = Id(await PostAsync(path + "/categories", new { name = "Assets", financialType = "Asset" }));
        var debts = Id(await PostAsync(path + "/categories", new { name = "Debts", financialType = "Debt" }));
        var groupPath = $"{path}/categories/{assets}/groups";
        var group = Id(await PostAsync(groupPath, new { name = "Investments" }));
        var loans = Id(await PostAsync($"{path}/categories/{debts}/groups", new { name = "Loans" }));
        var itemsPath = path + "/tracked-items";
        var pension = await CreateItemAsync("Pension", alex, group);
        var savings = Id(await PostAsync(itemsPath, new {
            groupId = group, holderPersonId = morgan, name = "Savings", currencyCode = "GBP", countryCode = "GB",
            includeInHousehold = true, allocations = new[] { Share(alex, 6000), Share(morgan, 4000) }
        }));
        var childSavings = await CreateItemAsync("Child savings", child, group, included: false);
        var isa = await CreateItemAsync("SA fund", alex, group, currency: "ZAR", country: "ZA");
        var loan = Id(await PostAsync(itemsPath, new {
            groupId = loans, holderPersonId = alex, name = "Loan", currencyCode = "GBP", countryCode = "GB",
            includeInHousehold = true, allocations = new[] { Share(alex, 2500), Share(morgan, 7500) }
        }));
        var house = Id(await PostAsync(path + "/financed-assets", new {
            groupId = group, name = "House", currencyCode = "GBP", countryCode = "GB",
            originalLoanAmount = 900m, hasWithdrawalFacility = true, includeInHousehold = true,
            owners = new[] { Share(alex, 2500), Share(morgan, 7500) }, loanHolderPersonIds = new[] { morgan },
            householdAllocations = new[] { Share(alex, 5000), Share(morgan, 5000) }
        }));
        var snapshotsPath = path + "/snapshots";
        var januaryJson = await PostAsync(snapshotsPath, new {
            snapshotDate = "2023-01-19", datePrecision = 2,
            values = new[] { Value(pension, 100m), Value(savings, 200m), Value(childSavings, 10m), Value(isa, 5m), Value(loan, 40m) },
            financedAssetValues = new[] {
                new { financedAssetId = house, balance = 1, amount = 1000m },
                new { financedAssetId = house, balance = 2, amount = 200m },
                new { financedAssetId = house, balance = 3, amount = 100m }
            }
        });
        var january = januaryJson.Deserialize<SnapshotResponse>(JsonSerializerOptions.Web)!;
        Assert.Equal(new DateOnly(2023, 1, 1), january.SnapshotDate);
        var januaryPath = $"{snapshotsPath}/{january.Id}";
        await AssertReportsAsync(januaryPath, alex, morgan, child,
            full: 1070m, personalAlex: 260m, personalMorgan: 800m, household: 1060m, allocatedAlex: 610m, allocatedMorgan: 450m);

        // New setup applies only to subsequent captures.
        await PutAsync($"{itemsPath}/{savings}/household-allocation", new {
            includeInHousehold = true, allocations = new[] { Share(alex, 10000) }
        });
        await PutAsync($"{itemsPath}/{pension}/retirement", new { isRetired = true });
        var replacement = await CreateItemAsync("Replacement fund", alex, group);
        await PutAsync($"{groupPath}/{group}", new { name = "Long-term funds" });
        var februaryJson = await PostAsync(snapshotsPath + "/next", new {
            snapshotDate = "2023-02-15", datePrecision = 2, sourceSnapshotId = january.Id,
            values = new[] { Value(replacement, 120m), Value(savings, 250m) }
        });
        var february = februaryJson.Deserialize<SnapshotResponse>(JsonSerializerOptions.Web)!;
        Assert.Equal(new DateOnly(2023, 2, 1), february.SnapshotDate);
        Assert.DoesNotContain(february.ItemSnapshots, item => item.TrackedItemId == pension);
        Assert.Equal(120m, Assert.Single(february.ItemSnapshots, item => item.TrackedItemId == replacement).Amount);
        var carriedLoan = Assert.Single(february.ItemSnapshots, item => item.TrackedItemId == loan);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, carriedLoan.ValueOrigin);
        Assert.Equal(new DateOnly(2023, 1, 1), carriedLoan.LastConfirmedDate);
        Assert.Equal(Assert.Single(january.ItemSnapshots, item => item.TrackedItemId == loan).Id, carriedLoan.CarriedFromItemSnapshotId);
        Assert.All(february.ItemSnapshots.Where(item => item.GroupId == group), item => Assert.Equal("Long-term funds", item.GroupName));
        var februaryPath = $"{snapshotsPath}/{february.Id}";
        await AssertReportsAsync(februaryPath, alex, morgan, child,
            full: 1140m, personalAlex: 280m, personalMorgan: 850m, household: 1130m, allocatedAlex: 760m, allocatedMorgan: 370m);

        // Correct the old capture after setup changes and creation of a newer capture.
        var correctedJson = await SendAsync(HttpMethod.Patch, januaryPath + "/values", new {
            expectedRevision = january.Revision, values = new[] { Value(savings, 220m), Value(loan, 80m) }
        }, HttpStatusCode.OK);
        var corrected = correctedJson.Deserialize<SnapshotResponse>(JsonSerializerOptions.Web)!;
        Assert.NotEqual(january.Revision, corrected.Revision);
        Assert.Contains(corrected.ItemSnapshots, item => item.TrackedItemId == pension);
        Assert.DoesNotContain(corrected.ItemSnapshots, item => item.TrackedItemId == replacement);
        Assert.All(corrected.ItemSnapshots.Where(item => item.GroupId == group), item => Assert.Equal("Investments", item.GroupName));
        var savedSavings = Assert.Single(corrected.ItemSnapshots, item => item.TrackedItemId == savings);
        Assert.Equal(2, savedSavings.HouseholdAllocations.Count);
        Assert.Equal(80m, Assert.Single(corrected.ItemSnapshots, item => item.TrackedItemId == loan).Amount);
        await AssertReportsAsync(januaryPath, alex, morgan, child,
            full: 1050m, personalAlex: 220m, personalMorgan: 820m, household: 1040m, allocatedAlex: 612m, allocatedMorgan: 428m);
        using var independent = _factory.CreateClient();
        var savedFebruary = await independent.GetFromJsonAsync<SnapshotResponse>(februaryPath);
        Assert.NotNull(savedFebruary);
        var savedCarriedLoan = Assert.Single(savedFebruary.ItemSnapshots, item => item.TrackedItemId == loan);
        Assert.Equal(40m, savedCarriedLoan.Amount);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, savedCarriedLoan.ValueOrigin);
        Assert.Equal(carriedLoan.CarriedFromItemSnapshotId, savedCarriedLoan.CarriedFromItemSnapshotId);
        Assert.Equivalent(february, savedFebruary);
        await AssertReportsAsync(februaryPath, alex, morgan, child,
            full: 1140m, personalAlex: 280m, personalMorgan: 850m, household: 1130m, allocatedAlex: 760m, allocatedMorgan: 370m);
        var inventory = await GetAsync(snapshotsPath);
        Assert.Equal(new[] { february.Id, january.Id }, inventory.EnumerateArray().Select(Id));

        async Task<Guid> CreateItemAsync(string name, Guid holder, Guid groupId, bool included = true,
            string currency = "GBP", string country = "GB") =>
            Id(await PostAsync(itemsPath, new {
                groupId, holderPersonId = holder, name, currencyCode = currency, countryCode = country, includeInHousehold = included
            }));
    }

    private async Task AssertReportsAsync(string snapshotPath, Guid alex, Guid morgan, Guid child,
        decimal full, decimal personalAlex, decimal personalMorgan, decimal household, decimal allocatedAlex, decimal allocatedMorgan)
    {
        var overview = await GetAsync(snapshotPath + "/summary");
        var individual = await GetAsync(snapshotPath + "/individual-holdings");
        var contributions = await GetAsync(snapshotPath + "/household-summary");
        Assert.Equal(overview.GetProperty("revision").GetGuid(), individual.GetProperty("revision").GetGuid());
        Assert.Equal(overview.GetProperty("revision").GetGuid(), contributions.GetProperty("revision").GetGuid());
        AssertNet(Currency(overview, "GBP"), "allRecordedTotals", full);
        AssertNet(Currency(overview, "ZAR"), "allRecordedTotals", 5m);
        AssertNet(Currency(Person(individual, alex), "GBP"), "holdingsTotals", personalAlex);
        AssertNet(Currency(Person(individual, morgan), "GBP"), "holdingsTotals", personalMorgan);
        AssertNet(Currency(Person(individual, child), "GBP"), "holdingsTotals", 10m);
        AssertNet(Currency(Person(individual, alex), "ZAR"), "holdingsTotals", 5m);
        AssertNet(Currency(contributions, "GBP"), "totals", household);
        AssertNet(Currency(contributions, "ZAR"), "totals", 5m);
        AssertNet(Currency(Person(contributions, alex), "GBP"), "totals", allocatedAlex);
        AssertNet(Currency(Person(contributions, morgan), "GBP"), "totals", allocatedMorgan);
        Assert.Empty(Person(contributions, child).GetProperty("currencies").EnumerateArray());
        AssertReconciles(overview, "allRecordedTotals", individual, "holdingsTotals");
        AssertReconciles(contributions, "totals", contributions, "totals");
    }

    private static void AssertReconciles(JsonElement aggregate, string aggregateTotalsName,
        JsonElement peopleReport, string personTotalsName)
    {
        var currencies = aggregate.GetProperty("currencies").EnumerateArray().ToArray();
        var personalCurrencies = peopleReport.GetProperty("people").EnumerateArray()
            .SelectMany(person => person.GetProperty("currencies").EnumerateArray()).ToArray();
        Assert.Equal(currencies.Select(currency => currency.GetProperty("currencyCode").GetString()).Order(),
            personalCurrencies.Select(currency => currency.GetProperty("currencyCode").GetString()).Distinct().Order());
        foreach (var currency in currencies)
        {
            var code = currency.GetProperty("currencyCode").GetString();
            var aggregateTotals = currency.GetProperty(aggregateTotalsName);
            var personalTotals = personalCurrencies.Where(row => row.GetProperty("currencyCode").GetString() == code)
                .Select(row => row.GetProperty(personTotalsName)).ToArray();
            Assert.Equal(aggregateTotals.GetProperty("knownNetPosition").GetDecimal(),
                personalTotals.Sum(total => total.GetProperty("knownNetPosition").GetDecimal()));
            foreach (var component in new[] { "ordinaryAssets", "ordinaryDebts", "financedAssetEquity" })
            {
                Assert.Equal(aggregateTotals.GetProperty(component).GetProperty("knownSubtotal").GetDecimal(),
                    personalTotals.Sum(total => total.GetProperty(component).GetProperty("knownSubtotal").GetDecimal()));
            }
        }
    }

    private static void AssertNet(JsonElement scope, string totalsProperty, decimal expected)
    {
        var totals = scope.GetProperty(totalsProperty);
        Assert.Equal(expected, totals.GetProperty("knownNetPosition").GetDecimal());
        Assert.True(totals.GetProperty("isComplete").GetBoolean());
    }

    private Task<JsonElement> PostAsync(string path, object body) => SendAsync(HttpMethod.Post, path, body, HttpStatusCode.Created);
    private Task<JsonElement> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body, HttpStatusCode.OK);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object body, HttpStatusCode expectedStatus)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await _client.SendAsync(request);
        Assert.Equal(expectedStatus, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (expectedStatus == HttpStatusCode.Created)
        {
            Assert.NotNull(response.Headers.Location);
            using var fetched = await _client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
            var retrieved = await fetched.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Id(result), Id(retrieved));
        }
        return result;
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        using var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object Share(Guid personId, int shareBasisPoints) => new { personId, shareBasisPoints };
    private static object Value(Guid trackedItemId, decimal amount) => new { trackedItemId, amount };
    private static Guid Id(JsonElement value) => value.GetProperty("id").GetGuid();
    private static JsonElement Currency(JsonElement report, string code) => report.GetProperty("currencies")
        .EnumerateArray().Single(row => row.GetProperty("currencyCode").GetString() == code);
    private static JsonElement Person(JsonElement report, Guid id) => report.GetProperty("people")
        .EnumerateArray().Single(row => row.GetProperty("person").GetProperty("personId").GetGuid() == id);
}
