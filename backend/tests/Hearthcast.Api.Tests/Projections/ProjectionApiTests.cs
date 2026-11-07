using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Hearthcast.Api.Projections.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Classification;
using Hearthcast.Data.ExchangeRates;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.Projections;

public sealed class ProjectionApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private readonly CountingRates _rates = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new(_database, _rates);
        _client = _factory.CreateClient();
    }
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    private sealed record Data(Guid Home, Guid Group, Guid Item, Guid Asset, Snapshot Snapshot);
    private async Task<Data> Setup(decimal? amount = 100, decimal? debt = 250)
    {
        var h = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var person = h.People.Single().Id;
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(h.Id, "Assets", FinancialType.Asset);
        var group = await classification.CreateGroupAsync(h.Id, category.Id, "Investments");
        var item = await new TrackedItemService(_database).CreateAsync(h.Id, group.Id, person, "Investment", "ZAR", "ZA", true);
        var asset = await new FinancedAssetService(_database).CreateAsync(h.Id, group.Id, "Home", "ZAR", "ZA",
            null, null, 1000, false, [new(person, 10000)], [person], true, [new(person, 10000)]);
        var snapshot = await new SnapshotService(_database).CreateAsync(h.Id, new(2023, 1, 1), [new(item.Id, amount)],
            financedAssetValues: [new(asset.Id, FinancedAssetBalanceKind.CurrentValuation, 1000),
                new(asset.Id, FinancedAssetBalanceKind.OutstandingLoanBalance, debt)]);
        return new(h.Id, group.Id, item.Id, asset.Id, snapshot);
    }
    private static string Presets(Guid home) => $"/api/households/{home}/projection-presets";
    private static string Run(Data d) => $"/api/households/{d.Home}/snapshots/{d.Snapshot.Id}/projection";
    private static JsonObject Inputs(Data d) => new()
    {
        ["assumptions"] = new JsonObject
        {
            ["currencyCode"] = " zar ", ["durationYears"] = 1,
            ["monthlyInvestmentContribution"] = 10, ["annualInvestmentReturnPercent"] = 0,
            ["monthlySavingsContribution"] = 5, ["annualSavingsInterestPercent"] = 0,
            ["annualInflationPercent"] = 0, ["showInflationAdjustedValues"] = false
        },
        ["groups"] = new JsonArray(new JsonObject { ["groupId"] = d.Group.ToString(), ["bucket"] = "Investment" }),
        ["financedAssets"] = new JsonArray(new JsonObject
        {
            ["financedAssetId"] = d.Asset.ToString(), ["monthlyLoanPaymentInAssetCurrency"] = 100,
            ["annualLoanInterestPercent"] = 0, ["annualAssetAppreciationPercent"] = 0
        })
    };
    private static JsonObject Save(Data d, string name = "Baseline") => new() { ["name"] = name, ["inputs"] = Inputs(d) };

    [Fact]
    public async Task PresetCrudReturnsLocationNormalisedValuesAndWholeReplacements()
    {
        var d = await Setup();
        using var created = await _client.PostAsJsonAsync(Presets(d.Home), Save(d, " Baseline "));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var preset = (await created.Content.ReadFromJsonAsync<ProjectionPresetResponse>())!;
        Assert.Equal("Baseline", preset.Name);
        Assert.Equal("ZAR", preset.Inputs.Assumptions.CurrencyCode);
        Assert.Equal("Investment", Assert.Single(preset.Inputs.Groups).Bucket);
        using var get = await _client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var changed = Save(d, "Changed");
        changed["inputs"]!["groups"]![0]!["bucket"] = "Savings";
        changed["inputs"]!["financedAssets"] = new JsonArray();
        using var update = await _client.PutAsJsonAsync(created.Headers.Location, changed);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = (await update.Content.ReadFromJsonAsync<ProjectionPresetResponse>())!;
        Assert.Equal(preset.Id, saved.Id);
        Assert.Equal(preset.CreatedAtUtc.ToUnixTimeMilliseconds(), saved.CreatedAtUtc.ToUnixTimeMilliseconds());
        Assert.Equal("Savings", Assert.Single(saved.Inputs.Groups).Bucket);
        Assert.Empty(saved.Inputs.FinancedAssets);
        var list = (await _client.GetFromJsonAsync<ProjectionPresetResponse[]>(Presets(d.Home)))!;
        Assert.Equal("Changed", Assert.Single(list).Name);
        using var deleted = await _client.DeleteAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await _client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<ProjectionPresetResponse[]>(Presets(d.Home)))!);
    }

    [Fact]
    public async Task UnsavedRunReturnsMonthlyPayoffReinvestmentAndNeverWritesOrFetchesRates()
    {
        var d = await Setup();
        using var response = await _client.PostAsJsonAsync(Run(d), new JsonObject { ["inputs"] = Inputs(d) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ProjectionRunResponse>())!;
        Assert.True(result.CanCalculate);
        Assert.Equal("ZAR", result.CurrencyCode);
        Assert.Equal(d.Snapshot.Revision, result.SnapshotRevision);
        Assert.Equal(13, result.Months!.Count);
        Assert.Equal(850, result.Months[0].Nominal.NetPosition);
        Assert.Equal(50, result.Months[3].RedirectedToInvestments);
        Assert.Equal(new DateOnly(2023, 4, 1), result.Months[3].FinancedAssets[0].PayoffDate);
        Assert.Equal(100, result.Months[4].RedirectedToInvestments);
        Assert.Equal(1170, result.Months[^1].Nominal.Investments);
        Assert.Equal(60, result.Months[^1].Nominal.Savings);
        Assert.Equal(2, result.Coverage.Count);
        Assert.Contains(result.Coverage, c => c.Disposition == "Investment");
        Assert.Empty(result.Issues);
        Assert.Empty(result.Warnings);
        Assert.Equal(0, _rates.Calls);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.ProjectionPresets.ToArrayAsync());
        Assert.Equal(d.Snapshot.Revision, (await context.Snapshots.SingleAsync()).Revision);
        Assert.Single(await context.Snapshots.ToArrayAsync());
    }

    [Fact]
    public async Task SavedAndUnsavedRunsAgreeAndPresetRemainsUnchanged()
    {
        var d = await Setup();
        using var create = await _client.PostAsJsonAsync(Presets(d.Home), Save(d));
        var preset = (await create.Content.ReadFromJsonAsync<ProjectionPresetResponse>())!;
        using var saved = await _client.PostAsJsonAsync(Run(d), new { presetId = preset.Id });
        using var unsaved = await _client.PostAsJsonAsync(Run(d), new JsonObject { ["inputs"] = Inputs(d) });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(await unsaved.Content.ReadAsStringAsync(), await saved.Content.ReadAsStringAsync());
        var after = (await _client.GetFromJsonAsync<ProjectionPresetResponse>(create.Headers.Location))!;
        Assert.Equal(preset.UpdatedAtUtc.ToUnixTimeMilliseconds(), after.UpdatedAtUtc.ToUnixTimeMilliseconds());
    }

    [Theory]
    [InlineData("MissingBalance", "MissingValue")]
    [InlineData("MissingLoan", "MissingValue")]
    [InlineData("MissingRate", "MissingExchangeRate")]
    [InlineData("MissingGroup", "MissingGroup")]
    [InlineData("MissingAsset", "MissingFinancedAsset")]
    public async Task IncompleteSnapshotReturnsDiagnosticsWithNoPartialCalculation(string situation, string issue)
    {
        var d = await Setup(amount: situation == "MissingBalance" ? null : 100, debt: situation == "MissingLoan" ? null : 250);
        var inputs = Inputs(d);
        if (situation == "MissingRate") inputs["assumptions"]!["currencyCode"] = "GBP";
        if (situation == "MissingGroup") inputs["groups"]![0]!["groupId"] = Guid.NewGuid().ToString();
        if (situation == "MissingAsset") inputs["financedAssets"]![0]!["financedAssetId"] = Guid.NewGuid().ToString();
        using var response = await _client.PostAsJsonAsync(Run(d), new JsonObject { ["inputs"] = inputs });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ProjectionRunResponse>())!;
        Assert.False(result.CanCalculate);
        Assert.Null(result.Months);
        Assert.Contains(result.Issues, i => i.Kind == issue && i.Severity == "Error");
        Assert.Empty(result.Warnings);
        Assert.Equal(0, _rates.Calls);
    }

    [Fact]
    public async Task CarriedWarningsAndNonAmortisingLoansAreReturnedAsReadableCodes()
    {
        var d = await Setup();
        var next = await new SnapshotService(_database).CreateNextAsync(d.Home, new(2023, 2, 1));
        var input = Inputs(d);
        input["financedAssets"]![0]!["monthlyLoanPaymentInAssetCurrency"] = 0;
        using var response = await _client.PostAsJsonAsync(Run(d with { Snapshot = next }), new JsonObject { ["inputs"] = input });
        var result = (await response.Content.ReadFromJsonAsync<ProjectionRunResponse>())!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.CanCalculate);
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, i => Assert.Equal("Warning", i.Severity));
        Assert.Equal("PaymentDoesNotReduceLoanBalance", Assert.Single(result.Warnings).Kind);
    }

    [Fact]
    public async Task HouseholdBoundariesAndUnknownResourcesReturnNotFound()
    {
        var d = await Setup();
        var other = await Setup();
        using var create = await _client.PostAsJsonAsync(Presets(d.Home), Save(d));
        var preset = (await create.Content.ReadFromJsonAsync<ProjectionPresetResponse>())!;
        var wrong = $"{Presets(other.Home)}/{preset.Id}";
        using var get = await _client.GetAsync(wrong);
        using var update = await _client.PutAsJsonAsync(wrong, Save(other));
        using var delete = await _client.DeleteAsync(wrong);
        using var runPreset = await _client.PostAsJsonAsync(Run(other), new { presetId = preset.Id });
        using var runSnapshot = await _client.PostAsJsonAsync(Run(d with { Home = other.Home }), new JsonObject { ["inputs"] = Inputs(other) });
        using var listUnknown = await _client.GetAsync(Presets(Guid.NewGuid()));
        using var createForeignGroup = await _client.PostAsJsonAsync(Presets(other.Home), Save(d));
        Assert.All(new[] { get, update, delete, runPreset, runSnapshot, listUnknown, createForeignGroup }, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
        var foreignAsset = Save(other);
        foreignAsset["inputs"]!["financedAssets"]![0]!["financedAssetId"] = d.Asset.ToString();
        using var asset = await _client.PostAsJsonAsync(Presets(other.Home), foreignAsset);
        Assert.Equal(HttpStatusCode.NotFound, asset.StatusCode);
    }

    [Fact]
    public async Task DuplicatePresetNamesConflictWithoutChangingExistingPreset()
    {
        var d = await Setup();
        using var first = await _client.PostAsJsonAsync(Presets(d.Home), Save(d));
        using var duplicate = await _client.PostAsJsonAsync(Presets(d.Home), Save(d));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Single((await _client.GetFromJsonAsync<ProjectionPresetResponse[]>(Presets(d.Home)))!);
    }

    [Theory]
    [InlineData("NoMode")]
    [InlineData("BothModes")]
    [InlineData("EmptyPreset")]
    [InlineData("NoAssumptions")]
    [InlineData("NoCurrency")]
    [InlineData("NoYears")]
    [InlineData("NoToggle")]
    [InlineData("NoContribution")]
    [InlineData("NoGroups")]
    [InlineData("NoAssets")]
    [InlineData("NullGroup")]
    [InlineData("NullAsset")]
    [InlineData("DuplicateGroup")]
    [InlineData("DuplicateAsset")]
    [InlineData("EmptyGroup")]
    [InlineData("EmptyAsset")]
    [InlineData("NumericBucket")]
    [InlineData("UnknownBucket")]
    [InlineData("MissingPayment")]
    [InlineData("NegativeContribution")]
    [InlineData("InvalidRate")]
    [InlineData("ExtraRoot")]
    [InlineData("ExtraAssumption")]
    [InlineData("ExtraGroup")]
    public async Task InvalidRunBodiesReturnBadRequestInsteadOfApplyingDefaults(string situation)
    {
        var d = await Setup();
        var input = Inputs(d);
        var request = new JsonObject { ["inputs"] = input };
        switch (situation)
        {
            case "NoMode": request.Remove("inputs"); break;
            case "BothModes": request["presetId"] = Guid.NewGuid().ToString(); break;
            case "EmptyPreset": request.Remove("inputs"); request["presetId"] = Guid.Empty.ToString(); break;
            case "NoAssumptions": input.Remove("assumptions"); break;
            case "NoCurrency": input["assumptions"]!.AsObject().Remove("currencyCode"); break;
            case "NoYears": input["assumptions"]!.AsObject().Remove("durationYears"); break;
            case "NoToggle": input["assumptions"]!.AsObject().Remove("showInflationAdjustedValues"); break;
            case "NoContribution": input["assumptions"]!.AsObject().Remove("monthlyInvestmentContribution"); break;
            case "NoGroups": input.Remove("groups"); break;
            case "NoAssets": input.Remove("financedAssets"); break;
            case "NullGroup": input["groups"] = new JsonArray((JsonNode?)null); break;
            case "NullAsset": input["financedAssets"] = new JsonArray((JsonNode?)null); break;
            case "DuplicateGroup": input["groups"]!.AsArray().Add(input["groups"]![0]!.DeepClone()); break;
            case "DuplicateAsset": input["financedAssets"]!.AsArray().Add(input["financedAssets"]![0]!.DeepClone()); break;
            case "EmptyGroup": input["groups"]![0]!["groupId"] = Guid.Empty.ToString(); break;
            case "EmptyAsset": input["financedAssets"]![0]!["financedAssetId"] = Guid.Empty.ToString(); break;
            case "NumericBucket": input["groups"]![0]!["bucket"] = 1; break;
            case "UnknownBucket": input["groups"]![0]!["bucket"] = "Other"; break;
            case "MissingPayment": input["financedAssets"]![0]!.AsObject().Remove("monthlyLoanPaymentInAssetCurrency"); break;
            case "NegativeContribution": input["assumptions"]!["monthlyInvestmentContribution"] = -1; break;
            case "InvalidRate": input["assumptions"]!["annualInflationPercent"] = -100; break;
            case "ExtraRoot": request["extra"] = true; break;
            case "ExtraAssumption": input["assumptions"]!["extra"] = true; break;
            case "ExtraGroup": input["groups"]![0]!["extra"] = true; break;
        }
        using var response = await _client.PostAsJsonAsync(Run(d), request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.ProjectionPresets.ToArrayAsync());
        Assert.Equal(d.Snapshot.Revision, (await context.Snapshots.SingleAsync()).Revision);
    }

    [Fact]
    public async Task ExplicitEmptySelectionsAllowANewMoneyOnlyScenario()
    {
        var d = await Setup();
        var input = Inputs(d);
        input["groups"] = new JsonArray();
        input["financedAssets"] = new JsonArray();
        using var response = await _client.PostAsJsonAsync(Run(d), new JsonObject { ["inputs"] = input });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ProjectionRunResponse>())!;
        Assert.True(result.CanCalculate);
        Assert.Equal(0, result.Months![0].Nominal.NetPosition);
        Assert.Equal(180, result.Months[^1].Nominal.NetPosition);
        Assert.All(result.Coverage, c => Assert.Equal("NotSelected", c.Disposition));
    }

    [Fact]
    public async Task NumericLimitsReturnBadRequestAndInvalidUpdateDoesNotOverwritePreset()
    {
        var d = await Setup();
        var input = Inputs(d);
        input["assumptions"]!["durationYears"] = 100;
        input["assumptions"]!["annualInvestmentReturnPercent"] = 100;
        using var run = await _client.PostAsJsonAsync(Run(d), new JsonObject { ["inputs"] = input });
        Assert.Equal(HttpStatusCode.BadRequest, run.StatusCode);
        using var create = await _client.PostAsJsonAsync(Presets(d.Home), Save(d));
        var preset = (await create.Content.ReadFromJsonAsync<ProjectionPresetResponse>())!;
        var invalid = Save(d, "Changed");
        invalid["inputs"]!["assumptions"]!["monthlyInvestmentContribution"] = -1;
        using var update = await _client.PutAsJsonAsync(create.Headers.Location, invalid);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var after = (await _client.GetFromJsonAsync<ProjectionPresetResponse>(create.Headers.Location))!;
        Assert.Equal(preset.Name, after.Name);
        Assert.Equal(preset.Inputs.Assumptions, after.Inputs.Assumptions);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\"Baseline\"}")]
    [InlineData("{\"name\":\" \"}")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task MalformedOrIncompletePresetBodiesReturnBadRequest(string json)
    {
        var d = await Setup();
        using var response = await _client.PostAsync(Presets(d.Home), new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class CountingRates : IExchangeRateProvider
    {
        public int Calls { get; private set; }
        public string Source => "Test";
        public Task<IReadOnlyList<ExchangeRateQuote>> GetAsync(string baseCurrency, IReadOnlyList<string> quotes, DateOnly date, CancellationToken token)
        {
            Calls++;
            throw new InvalidOperationException("Projection requests must not fetch rates.");
        }
    }
}
