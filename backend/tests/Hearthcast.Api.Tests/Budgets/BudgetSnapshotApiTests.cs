using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hearthcast.Api.Budgets.Contracts;
using Hearthcast.Api.Households.Contracts;
using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Budgets;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hearthcast.Api.Tests.Budgets;

public sealed class BudgetSnapshotApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;
    private const string January = "2026-01-01";
    private const string CheckedOn = "2025-12-28";
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new(_database);
        _client = _factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
    }
    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }
    private sealed record Data(Guid Home, ExpenseCategoryResponse Category, MerchantResponse Merchant,
        BudgetItemResponse Energy, BudgetItemResponse Food)
    {
        internal string Root => Path(Home);
        internal string Outgoings => $"/api/households/{Home}/budgets/current-outgoings";
    }
    private static string Path(Guid home) => $"/api/households/{home}/budgets/snapshots";
    private static string PlanPath(Data d, BudgetSnapshotResponse plan) => d.Root + $"/{plan.Id}";
    private static string EntryPath(Data d, BudgetSnapshotResponse plan, BudgetEntryResponse? entry = null) =>
        PlanPath(d, plan) + $"/entries/{(entry ?? plan.Entries[0]).Id}";
    private async Task<Data> SeedAsync()
    {
        using var homeResponse = await _client.PostAsJsonAsync("/api/households", new { name = "Home", firstPersonDisplayName = "Alex" });
        homeResponse.EnsureSuccessStatusCode();
        var home = (await homeResponse.Content.ReadFromJsonAsync<HouseholdResponse>())!.Id;
        using var categoryResponse = await _client.PostAsJsonAsync($"/api/households/{home}/spending/categories", new { name = "Bills" });
        using var merchantResponse = await _client.PostAsJsonAsync($"/api/households/{home}/spending/merchants", new { name = "Supplier" });
        categoryResponse.EnsureSuccessStatusCode(); merchantResponse.EnsureSuccessStatusCode();
        var category = (await categoryResponse.Content.ReadFromJsonAsync<ExpenseCategoryResponse>())!;
        var merchant = (await merchantResponse.Content.ReadFromJsonAsync<MerchantResponse>())!;
        async Task<BudgetItemResponse> Item(string code, string currency, string frequency)
        {
            using var result = await _client.PostAsJsonAsync($"/api/households/{home}/budgets/items", new
            { expenseCategoryId = category.Id, code, name = code, currencyCode = currency, frequency, merchantId = merchant.Id, notes = "Usage" });
            result.EnsureSuccessStatusCode();
            return (await result.Content.ReadFromJsonAsync<BudgetItemResponse>())!;
        }
        return new(home, category, merchant, await Item("ENERGY", "GBP", "Month"), await Item("FOOD", "ZAR", "Week"));
    }
    private static Dictionary<string, object?> Entry(Guid item, string mode = "Confirm", decimal amount = 200m) =>
        mode.Equals("Confirm", StringComparison.OrdinalIgnoreCase)
            ? new() { ["budgetItemId"] = item, ["mode"] = mode, ["plannedAmount"] = amount, ["confirmedOn"] = CheckedOn }
            : new() { ["budgetItemId"] = item, ["mode"] = mode };
    private static Dictionary<string, object?> CreateBody(Data d, string date = January, Guid? id = null) => new()
    { ["id"] = id ?? Guid.NewGuid(), ["effectiveFrom"] = date, ["entries"] = new[] { Entry(d.Energy.Id) }, ["notes"] = "Plan" };
    private static Dictionary<string, object?> Metadata(BudgetEntryResponse entry) => new()
    {
        ["itemCode"] = entry.ItemCode, ["itemName"] = entry.ItemName, ["expenseCategoryId"] = entry.ExpenseCategoryId,
        ["expenseCategoryName"] = entry.ExpenseCategoryName, ["currencyCode"] = entry.CurrencyCode,
        ["frequency"] = entry.Frequency.ToString(), ["intervalCount"] = entry.IntervalCount,
        ["merchantId"] = entry.MerchantId, ["merchantName"] = entry.MerchantName, ["notes"] = entry.Notes
    };
    private async Task<BudgetSnapshotResponse> CaptureAsync(Data d, Dictionary<string, object?>? body = null)
    {
        using var response = await _client.PostAsJsonAsync(d.Root, body ?? CreateBody(d));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<BudgetSnapshotResponse>())!;
        Assert.Equal(PlanPath(d, plan), response.Headers.Location!.AbsolutePath);
        Assert.Equivalent(plan, await ReadAsync(d, plan.Id));
        return plan;
    }
    private async Task<BudgetSnapshotResponse> ReadAsync(Data d, Guid id)
    {
        using var response = await _client.GetAsync(d.Root + $"/{id}");
        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain("creationRequestHash", await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BudgetSnapshotResponse>())!;
    }
    private async Task<BudgetSnapshotResponse> CarryAsync(Data d, BudgetSnapshotResponse source, string date = "2026-02-01")
    {
        var body = CreateBody(d, date);
        body["copiedFromBudgetSnapshotId"] = source.Id;
        body["entries"] = new[] { Entry(d.Energy.Id, "Carry") };
        return await CaptureAsync(d, body);
    }
    private static async Task<BudgetSnapshotResponse> Saved(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<BudgetSnapshotResponse>())!;
        }
    }

    [Fact]
    public async Task CaptureHasLocationNamedEnumsAndDistinctEmptyUnrecordedZeroAndMixedCurrencyStates()
    {
        var d = await SeedAsync();
        var body = CreateBody(d);
        body["entries"] = new[] { Entry(d.Energy.Id, "cOnFiRm", 0), Entry(d.Food.Id, "Unrecorded") };
        var plan = await CaptureAsync(d, body);
        Assert.False(plan.IsComplete);
        Assert.Equal(0, plan.Entries.Single(e => e.BudgetItemId == d.Energy.Id).PlannedAmount);
        Assert.Null(plan.Entries.Single(e => e.BudgetItemId == d.Food.Id).PlannedAmount);
        var wire = await _client.GetFromJsonAsync<JsonElement>(PlanPath(d, plan));
        Assert.Equal("Month", wire.GetProperty("entries")[0].GetProperty("frequency").GetString());
        Assert.Equal("Confirmed", wire.GetProperty("entries")[0].GetProperty("valueOrigin").GetString());
        Assert.False(wire.TryGetProperty("totalAmount", out _));
        body = CreateBody(d, "2026-02-01"); body["entries"] = Array.Empty<object>(); body["copiedFromBudgetSnapshotId"] = plan.Id;
        var empty = await CaptureAsync(d, body);
        Assert.True(empty.IsComplete); Assert.Empty(empty.Entries);
        Assert.Equal(plan.Id, empty.CopiedFromBudgetSnapshotId);
        body = CreateBody(d, "2026-03-01"); body["entries"] = new[] { Entry(d.Energy.Id, amount: 0.000001m), Entry(d.Food.Id, amount: 999_999_999_999.999999m) };
        var mixed = await CaptureAsync(d, body);
        Assert.True(mixed.IsComplete);
        Assert.Equal(new[] { "GBP", "ZAR" }, mixed.Entries.Select(e => e.CurrencyCode).Order());
        await Problem(await _client.DeleteAsync(PlanPath(d, mixed)), 405);
    }

    [Fact]
    public async Task CorrectionEndpointsKeepMetadataEditsConfirmationExplicitRecopyAndRemovalSeparate()
    {
        var d = await SeedAsync();
        var source = await CaptureAsync(d);
        var child = await CarryAsync(d, source);
        source = await Saved(await _client.PutAsJsonAsync(EntryPath(d, source) + "/amount",
            new { expectedRevision = source.Revision, plannedAmount = 250m, confirmedOn = "2026-01-10", notes = "Corrected source" }));
        var metadata = Metadata(child.Entries[0]); metadata["itemName"] = "Historical description";
        child = await Saved(await _client.PutAsJsonAsync(EntryPath(d, child) + "/metadata", new { expectedRevision = child.Revision, metadata }));
        Assert.Equal(200, child.Entries[0].PlannedAmount);
        Assert.Equal(BudgetValueOrigin.CarriedForward, child.Entries[0].ValueOrigin);
        Assert.Equal(new DateOnly(2025, 12, 28), child.Entries[0].LastConfirmedDate);
        Assert.Equal(source.Entries[0].Id, child.Entries[0].CarriedFromBudgetEntryId);
        child = await Saved(await _client.PutAsJsonAsync(EntryPath(d, child) + "/amount",
            new { expectedRevision = child.Revision, plannedAmount = 200, confirmedOn = "2026-02-02", notes = (string?)null }));
        Assert.Equal(BudgetValueOrigin.Confirmed, child.Entries[0].ValueOrigin);
        Assert.Null(child.Entries[0].CarriedFromBudgetEntryId);
        var recopy = Entry(d.Energy.Id, "Carry"); recopy["source"] = new { snapshotId = source.Id, entryId = source.Entries[0].Id };
        child = await Saved(await _client.PutAsJsonAsync(EntryPath(d, child), new { expectedRevision = child.Revision, entry = recopy }));
        Assert.Equal(250, child.Entries[0].PlannedAmount);
        child = await Saved(await _client.PostAsJsonAsync(PlanPath(d, child) + "/entries",
            new { expectedRevision = child.Revision, entry = Entry(d.Food.Id, "Unrecorded") }));
        Assert.False(child.IsComplete);
        var food = child.Entries.Single(e => e.BudgetItemId == d.Food.Id);
        child = await Saved(await _client.PutAsJsonAsync(EntryPath(d, child, food),
            new { expectedRevision = child.Revision, entry = Entry(d.Food.Id, amount: 0) }));
        Assert.True(child.IsComplete);
        child = await Saved(await _client.PostAsJsonAsync(EntryPath(d, child, food) + "/remove", new { expectedRevision = child.Revision }));
        Assert.Single(child.Entries);
        child = await Saved(await _client.PutAsJsonAsync(PlanPath(d, child) + "/header",
            new { expectedRevision = child.Revision, effectiveFrom = "2026-02-15", notes = (string?)null }));
        Assert.Null(child.Notes);
        Assert.Equal(new DateOnly(2026, 2, 15), child.EffectiveFrom);
        child = await Saved(await _client.PutAsJsonAsync(EntryPath(d, child) + "/amount",
            new { expectedRevision = child.Revision, plannedAmount = (decimal?)null, confirmedOn = (string?)null, notes = (string?)null }));
        Assert.Equal(BudgetValueOrigin.Unrecorded, child.Entries[0].ValueOrigin);
        Assert.Null(child.Entries[0].LastConfirmedDate);
    }

    [Fact]
    public async Task SourceRemovalAndDateCorrectionExposeDetachedProvenanceAndAdvanceReceivingRevisions()
    {
        var d = await SeedAsync();
        var source = await CaptureAsync(d);
        var child = await CarryAsync(d, source);
        var grandchild = await CarryAsync(d, child, "2026-03-01");
        var corrected = await Saved(await _client.PutAsJsonAsync(PlanPath(d, child) + "/header",
            new { expectedRevision = child.Revision, effectiveFrom = "2026-04-01", notes = "Date correction" }));
        var detached = await ReadAsync(d, grandchild.Id);
        Assert.Null(detached.CopiedFromBudgetSnapshotId);
        Assert.Null(detached.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(200, detached.Entries[0].PlannedAmount);
        Assert.Equal(BudgetValueOrigin.CarriedForward, detached.Entries[0].ValueOrigin);
        Assert.NotEqual(grandchild.Revision, detached.Revision);
        var problem = await Problem(await _client.PutAsJsonAsync(EntryPath(d, grandchild) + "/amount",
            new { expectedRevision = grandchild.Revision, plannedAmount = 20, confirmedOn = CheckedOn, notes = (string?)null }), 409);
        Assert.Equal("Budget snapshot revision conflict", problem.GetProperty("title").GetString());
        var empty = await Saved(await _client.PostAsJsonAsync(EntryPath(d, source) + "/remove", new { expectedRevision = source.Revision }));
        Assert.Empty(empty.Entries); Assert.True(empty.IsComplete);
        var receiving = await ReadAsync(d, corrected.Id);
        Assert.Null(receiving.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(source.Id, receiving.CopiedFromBudgetSnapshotId);
        Assert.NotEqual(corrected.Revision, receiving.Revision);
    }

    [Fact]
    public async Task CreateRetriesReturn200WithRetainedCurrentStateAfterSourcesAndOriginalDateChange()
    {
        var d = await SeedAsync();
        var source = await CaptureAsync(d);
        var body = CreateBody(d, "2026-02-01"); body["notes"] = " Notes "; body["copiedFromBudgetSnapshotId"] = source.Id;
        body["entries"] = new[] { Entry(d.Energy.Id, "Carry") };
        var first = await CaptureAsync(d, body);
        await Saved(await _client.PostAsJsonAsync(EntryPath(d, source) + "/remove", new { expectedRevision = source.Revision }));
        var plan = await ReadAsync(d, first.Id);
        plan = await Saved(await _client.PostAsJsonAsync(EntryPath(d, plan) + "/remove", new { expectedRevision = plan.Revision }));
        plan = await Saved(await _client.PutAsJsonAsync(PlanPath(d, plan) + "/header",
            new { expectedRevision = plan.Revision, effectiveFrom = "2025-12-01", notes = "Backdated" }));
        body["notes"] = "Notes";
        using var retry = await _client.PostAsJsonAsync(d.Root, body);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Null(retry.Headers.Location);
        Assert.Equivalent(plan, await retry.Content.ReadFromJsonAsync<BudgetSnapshotResponse>());
        Assert.Equal(first.CreatedAtUtc, plan.CreatedAtUtc);
        body["notes"] = "Different request";
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 409);
        await Problem(await _client.PostAsJsonAsync(d.Root, CreateBody(d, "2025-12-01")), 409);
        Assert.Equal(2, (await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root))!.Records.Count);
    }

    [Fact]
    public async Task CopyUsesSavedMetadataUnlessSetupRefreshOrHistoricalMetadataIsExplicitlySelected()
    {
        var d = await SeedAsync(); var source = await CaptureAsync(d);
        using var updated = await _client.PutAsJsonAsync($"/api/households/{d.Home}/budgets/items/{d.Energy.Id}/details", new
        { expenseCategoryId = d.Category.Id, name = "Current energy", merchantId = (Guid?)null, currencyCode = "USD", frequency = "Year", intervalCount = 3, notes = "New defaults" });
        updated.EnsureSuccessStatusCode();
        var copied = await CarryAsync(d, source);
        Assert.Equal("GBP", copied.Entries[0].CurrencyCode);
        Assert.Equal(BudgetFrequency.Month, copied.Entries[0].Frequency);
        Assert.Equal("ENERGY", copied.Entries[0].ItemName);
        var entry = Entry(d.Energy.Id, "Carry"); entry["refreshFromSetup"] = true;
        var body = CreateBody(d, "2026-03-01"); body["copiedFromBudgetSnapshotId"] = source.Id; body["entries"] = new[] { entry };
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        entry = Entry(d.Energy.Id, amount: 20); entry["refreshFromSetup"] = true; body["entries"] = new[] { entry };
        var refreshed = await CaptureAsync(d, body);
        Assert.Equal("USD", refreshed.Entries[0].CurrencyCode);
        Assert.Equal(BudgetFrequency.Year, refreshed.Entries[0].Frequency);
        Assert.Equal("Current energy", refreshed.Entries[0].ItemName);
        var metadata = Metadata(source.Entries[0]); metadata["itemName"] = "Historical name";
        entry = Entry(d.Energy.Id, amount: 100); entry["metadata"] = metadata;
        body = CreateBody(d, "2025-01-01"); body["entries"] = new[] { entry };
        var historical = await CaptureAsync(d, body);
        Assert.Equal("Historical name", historical.Entries[0].ItemName);
        Assert.Equal("GBP", historical.Entries[0].CurrencyCode);
    }

    [Fact]
    public async Task EffectiveAndCurrentOutgoingReadsSeparateUnknownEmptyOmittedAndSavedVersusCurrentDefaults()
    {
        var d = await SeedAsync();
        using var absent = await _client.GetAsync(d.Root + "/effective?asOf=" + January);
        Assert.Equal(HttpStatusCode.NoContent, absent.StatusCode); Assert.Empty(await absent.Content.ReadAsStringAsync());
        var unknown = (await _client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>(d.Outgoings + "?asOf=" + January))!;
        Assert.Null(unknown.SnapshotId); Assert.Null(unknown.IsComplete); Assert.All(unknown.Items, i => Assert.Null(i.Entry));
        var source = await CaptureAsync(d);
        var body = CreateBody(d, "2026-02-01"); body["entries"] = Array.Empty<object>();
        var empty = await CaptureAsync(d, body);
        using var update = await _client.PutAsJsonAsync($"/api/households/{d.Home}/budgets/items/{d.Energy.Id}/details", new
        { expenseCategoryId = d.Category.Id, name = "Current name", merchantId = (Guid?)null, currencyCode = "USD", frequency = "Year", intervalCount = 3, notes = (string?)null });
        update.EnsureSuccessStatusCode();
        var current = (await _client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>(d.Outgoings + "?asOf=" + January))!;
        var energy = current.Items.Single(i => i.Item.Id == d.Energy.Id);
        Assert.Equal("USD", energy.Item.CurrencyCode); Assert.Equal(BudgetFrequency.Year, energy.Item.Frequency);
        Assert.Equal("GBP", energy.Entry!.CurrencyCode); Assert.Equal(BudgetFrequency.Month, energy.Entry.Frequency);
        Assert.Equal(200, energy.Entry.PlannedAmount); Assert.Equal(source.Id, current.SnapshotId);
        Assert.Equal(empty.EffectiveFrom, current.EffectiveUntilExclusive);
        var knownEmpty = (await _client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>(d.Outgoings + "?asOf=2026-02-01"))!;
        Assert.Equal(empty.Id, knownEmpty.SnapshotId); Assert.True(knownEmpty.IsComplete);
        Assert.All(knownEmpty.Items, i => Assert.Null(i.Entry));
        var effective = (await _client.GetFromJsonAsync<BudgetEffectivePlanResponse>(d.Root + "/effective?asOf=2026-01-31"))!;
        Assert.Equal(source.Id, effective.Snapshot.Id); Assert.Equal(empty.EffectiveFrom, effective.EffectiveUntilExclusive);
        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        var today = (await _client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>(d.Outgoings))!;
        Assert.InRange(today.AsOf, before, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Theory]
    [InlineData("2026-06-30T23:59:59.9999999+00:00", "2026-06-30", "2026-07-01")]
    [InlineData("2026-07-01T00:00:00.0000000+00:00", "2026-07-01", "2026-06-30")]
    public async Task UtcMidnightDefaultsAreDeterministicAndExplicitAsOfBypassesTheClock(string instant, string expectedDate, string explicitDate)
    {
        var d = await SeedAsync();
        var june = await CaptureAsync(d, CreateBody(d, "2026-06-30"));
        var body = CreateBody(d, "2026-07-01"); body["entries"] = new[] { Entry(d.Energy.Id, amount: 20) };
        var july = await CaptureAsync(d, body);
        var clock = new FixedClock(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));
        await using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = app.CreateClient(new() { HandleCookies = false });
        var expected = expectedDate == "2026-06-30" ? june : july;
        var date = DateOnly.ParseExact(expectedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var effective = (await client.GetFromJsonAsync<BudgetEffectivePlanResponse>(d.Root + "/effective"))!;
        var outgoing = (await client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>(d.Outgoings))!;
        Assert.Equal(date, effective.AsOf); Assert.Equal(expected.Id, effective.Snapshot.Id);
        Assert.Equal(date, outgoing.AsOf); Assert.Equal(expected.Id, outgoing.SnapshotId);
        Assert.Equal(expected.Entries[0].PlannedAmount, outgoing.Items.Single(i => i.Item.Id == d.Energy.Id).Entry!.PlannedAmount);
        Assert.Equal(2, clock.Reads); // Exactly one clock read in each default-date action.
        expected = explicitDate == "2026-06-30" ? june : july;
        date = DateOnly.ParseExact(explicitDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        effective = (await client.GetFromJsonAsync<BudgetEffectivePlanResponse>(d.Root + "/effective?asOf=" + explicitDate))!;
        outgoing = (await client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>(d.Outgoings + "?asOf=" + explicitDate))!;
        Assert.Equal(date, effective.AsOf); Assert.Equal(expected.Id, effective.Snapshot.Id);
        Assert.Equal(date, outgoing.AsOf); Assert.Equal(expected.Id, outgoing.SnapshotId);
        Assert.Equal(2, clock.Reads); // Explicit dates never consult the fallback clock.
    }

    [Fact]
    public async Task HistoryPagesAndCopySuggestionsUseScopedCompletePlansAndExclusiveDates()
    {
        var d = await SeedAsync();
        var other = await SeedAsync(); await CaptureAsync(other);
        for (var month = 1; month <= 5; month++)
        {
            var body = CreateBody(d, $"2026-{month:D2}-01"); body["entries"] = new[] { Entry(d.Energy.Id), Entry(d.Food.Id, "Unrecorded") };
            await CaptureAsync(d, body);
        }
        var page = (await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root + "?pageSize=2"))!;
        Assert.Equal(2, page.PageSize); Assert.Equal(2, page.Records.Count);
        Assert.All(page.Records, p => Assert.Equal(2, p.Entries.Count));
        var second = (await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root + $"?pageSize=2&before={page.NextBefore:yyyy-MM-dd}"))!;
        var last = (await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root + $"?pageSize=2&before={second.NextBefore:yyyy-MM-dd}"))!;
        Assert.Single(last.Records); Assert.Null(last.NextBefore);
        Assert.Equal(5, page.Records.Concat(second.Records).Concat(last.Records).Select(s => s.Id).Distinct().Count());
        var suggestion = (await _client.GetFromJsonAsync<BudgetSnapshotResponse>(d.Root + "/copy-source?effectiveFrom=2026-03-01"))!;
        Assert.Equal(new DateOnly(2026, 2, 1), suggestion.EffectiveFrom);
        using var none = await _client.GetAsync(d.Root + "/copy-source?effectiveFrom=" + January);
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("effectiveFrom")]
    [InlineData("entries")]
    public async Task CaptureRequiresExplicitIdentityDateAndEntryCollection(string field)
    {
        var d = await SeedAsync(); var body = CreateBody(d); body.Remove(field);
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        Assert.Empty((await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root))!.Records);
    }

    [Theory]
    [InlineData("idNull")]
    [InlineData("idEmpty")]
    [InlineData("dateNull")]
    [InlineData("dateMin")]
    [InlineData("dateMax")]
    [InlineData("entriesNull")]
    [InlineData("entryNull")]
    [InlineData("duplicate")]
    [InlineData("templateEmpty")]
    [InlineData("templateSelf")]
    [InlineData("sourceSelf")]
    [InlineData("notesNul")]
    [InlineData("hash")]
    [InlineData("householdId")]
    [InlineData("revision")]
    public async Task InvalidCaptureHeadersAndCollectionsDoNotWritePartialPlans(string field)
    {
        var d = await SeedAsync(); var body = CreateBody(d);
        switch (field)
        {
            case "idNull": body["id"] = null; break;
            case "idEmpty": body["id"] = Guid.Empty; break;
            case "dateNull": body["effectiveFrom"] = null; break;
            case "dateMin": body["effectiveFrom"] = "0001-01-01"; break;
            case "dateMax": body["effectiveFrom"] = "9999-12-31"; break;
            case "entriesNull": body["entries"] = null; break;
            case "entryNull": body["entries"] = new object?[] { null }; break;
            case "duplicate": body["entries"] = new[] { Entry(d.Energy.Id), Entry(d.Energy.Id) }; break;
            case "templateEmpty": body["copiedFromBudgetSnapshotId"] = Guid.Empty; break;
            case "templateSelf": body["copiedFromBudgetSnapshotId"] = body["id"]; break;
            case "sourceSelf":
                var entry = Entry(d.Energy.Id, "Carry"); entry["source"] = new { snapshotId = body["id"], entryId = Guid.NewGuid() };
                body["entries"] = new[] { entry }; break;
            case "notesNul": body["notes"] = "bad\0notes"; break;
            case "hash": body["creationRequestHash"] = new string('a', 64); break;
            default: body[field] = Guid.NewGuid(); break;
        }
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        Assert.Empty((await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root))!.Records);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.BudgetEntries.ToArrayAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData("2")]
    [InlineData("Confirmed")]
    [InlineData("Confirm, Unrecorded")]
    [InlineData("unknown")]
    public async Task ValueModesAcceptOnlySingleDefinedTextNames(object? mode)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d);
        var entry = mode is 1 ? Entry(d.Energy.Id, "Unrecorded") : Entry(d.Energy.Id);
        if (mode is 3 or "Confirm, Unrecorded")
        {
            entry = Entry(d.Energy.Id, "Carry");
            entry["source"] = new { snapshotId = plan.Id, entryId = plan.Entries[0].Id };
        }
        entry["mode"] = mode;
        await InvalidEntryEverywhere(d, plan, entry);
    }

    [Theory]
    [InlineData("itemMissing")]
    [InlineData("itemEmpty")]
    [InlineData("modeMissing")]
    [InlineData("amountMissing")]
    [InlineData("amountNull")]
    [InlineData("dateMissing")]
    [InlineData("dateNull")]
    [InlineData("negative")]
    [InlineData("bound")]
    [InlineData("precision")]
    [InlineData("numericString")]
    [InlineData("dateMin")]
    [InlineData("dateMax")]
    [InlineData("unrecordedAmount")]
    [InlineData("carryAmount")]
    [InlineData("refreshNull")]
    [InlineData("readonlyOrigin")]
    public async Task InvalidEntryShapesAreRejectedForCaptureAdditionAndFullCorrection(string field)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d); var entry = Entry(d.Energy.Id);
        switch (field)
        {
            case "itemMissing": entry.Remove("budgetItemId"); break;
            case "itemEmpty": entry["budgetItemId"] = Guid.Empty; break;
            case "modeMissing": entry.Remove("mode"); break;
            case "amountMissing": entry.Remove("plannedAmount"); break;
            case "amountNull": entry["plannedAmount"] = null; break;
            case "dateMissing": entry.Remove("confirmedOn"); break;
            case "dateNull": entry["confirmedOn"] = null; break;
            case "negative": entry["plannedAmount"] = -1; break;
            case "bound": entry["plannedAmount"] = 1_000_000_000_000m; break;
            case "precision": entry["plannedAmount"] = 0.0000001m; break;
            case "numericString": entry["plannedAmount"] = "200"; break;
            case "dateMin": entry["confirmedOn"] = "0001-01-01"; break;
            case "dateMax": entry["confirmedOn"] = "9999-12-31"; break;
            case "unrecordedAmount": entry["mode"] = "Unrecorded"; break;
            case "carryAmount": entry["mode"] = "Carry"; break;
            case "refreshNull": entry["refreshFromSetup"] = null; break;
            case "readonlyOrigin": entry["valueOrigin"] = "Confirmed"; break;
        }
        await InvalidEntryEverywhere(d, plan, entry);
    }

    [Theory]
    [InlineData("snapshotMissing")]
    [InlineData("entryMissing")]
    [InlineData("snapshotEmpty")]
    [InlineData("entryEmpty")]
    [InlineData("snapshotNull")]
    [InlineData("unknown")]
    public async Task NonnullNestedSourcesRequireBothScopedIdentifiers(string field)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d);
        var source = new Dictionary<string, object?> { ["snapshotId"] = plan.Id, ["entryId"] = plan.Entries[0].Id };
        switch (field)
        {
            case "snapshotMissing": source.Remove("snapshotId"); break;
            case "entryMissing": source.Remove("entryId"); break;
            case "snapshotEmpty": source["snapshotId"] = Guid.Empty; break;
            case "entryEmpty": source["entryId"] = Guid.Empty; break;
            case "snapshotNull": source["snapshotId"] = null; break;
            case "unknown": source["expectedRevision"] = plan.Revision; break;
        }
        var entry = Entry(d.Energy.Id, "Carry"); entry["source"] = source;
        await InvalidEntryEverywhere(d, plan, entry);
    }

    [Theory]
    [InlineData("itemCode")]
    [InlineData("itemName")]
    [InlineData("expenseCategoryId")]
    [InlineData("expenseCategoryName")]
    [InlineData("currencyCode")]
    [InlineData("frequency")]
    [InlineData("intervalCount")]
    [InlineData("merchantId")]
    [InlineData("merchantName")]
    [InlineData("notes")]
    public async Task FullMetadataRequiresAllFieldsIncludingExplicitNullableContext(string missing)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d);
        var metadata = Metadata(plan.Entries[0]); metadata.Remove(missing);
        await InvalidMetadataEverywhere(d, plan, metadata);
    }

    [Theory]
    [InlineData("categoryEmpty")]
    [InlineData("nameBlank")]
    [InlineData("codeLong")]
    [InlineData("currencyShape")]
    [InlineData("frequencyNumber")]
    [InlineData("frequencyCombined")]
    [InlineData("intervalZero")]
    [InlineData("intervalString")]
    [InlineData("merchantPair")]
    [InlineData("notesNul")]
    [InlineData("unknown")]
    public async Task InvalidNestedMetadataNeverMutatesSavedContext(string field)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d); var metadata = Metadata(plan.Entries[0]);
        switch (field)
        {
            case "categoryEmpty": metadata["expenseCategoryId"] = Guid.Empty; break;
            case "nameBlank": metadata["itemName"] = " \u2000 "; break;
            case "codeLong": metadata["itemCode"] = new string('A', 65); break;
            case "currencyShape": metadata["currencyCode"] = "GB1"; break;
            case "frequencyNumber": metadata["frequency"] = 2; break;
            case "frequencyCombined": metadata["frequency"] = "Week, Month"; break;
            case "intervalZero": metadata["intervalCount"] = 0; break;
            case "intervalString": metadata["intervalCount"] = "1"; break;
            case "merchantPair": metadata["merchantId"] = null; break;
            case "notesNul": metadata["notes"] = "bad\0notes"; break;
            case "unknown": metadata["householdId"] = d.Home; break;
        }
        await InvalidMetadataEverywhere(d, plan, metadata);
    }

    [Theory]
    [InlineData("header", "expectedRevision")]
    [InlineData("header", "effectiveFrom")]
    [InlineData("header", "notes")]
    [InlineData("amount", "expectedRevision")]
    [InlineData("amount", "plannedAmount")]
    [InlineData("amount", "confirmedOn")]
    [InlineData("amount", "notes")]
    [InlineData("metadata", "expectedRevision")]
    [InlineData("metadata", "metadata")]
    [InlineData("entry", "expectedRevision")]
    [InlineData("entry", "entry")]
    [InlineData("remove", "expectedRevision")]
    public async Task MutationsRequireTheirOwnExplicitRevisionAndValueContracts(string kind, string missing)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d);
        var body = new Dictionary<string, object?> { ["expectedRevision"] = plan.Revision };
        switch (kind)
        {
            case "header": body["effectiveFrom"] = "2026-01-15"; body["notes"] = null; break;
            case "amount": body["plannedAmount"] = null; body["confirmedOn"] = null; body["notes"] = null; break;
            case "metadata": body["metadata"] = Metadata(plan.Entries[0]); break;
            case "entry": body["entry"] = Entry(d.Energy.Id); break;
        }
        body.Remove(missing);
        var path = kind == "header" ? PlanPath(d, plan) + "/header" : EntryPath(d, plan) + (kind == "entry" ? "" : "/" + kind);
        await Problem(kind == "remove" ? await _client.PostAsJsonAsync(path, body) : await _client.PutAsJsonAsync(path, body), 400);
        if (kind == "entry") await Problem(await _client.PostAsJsonAsync(PlanPath(d, plan) + "/entries", body), 400);
        Assert.Equivalent(plan, await ReadAsync(d, plan.Id));
    }

    [Theory]
    [InlineData("expectedRevision")]
    [InlineData("amount")]
    [InlineData("confirmedOn")]
    [InlineData("pair")]
    [InlineData("notes")]
    public async Task InvalidAmountCorrectionRollsBackTheEntireSavedPlan(string field)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d);
        var body = new Dictionary<string, object?> { ["expectedRevision"] = plan.Revision, ["plannedAmount"] = 20, ["confirmedOn"] = CheckedOn, ["notes"] = null };
        switch (field)
        {
            case "expectedRevision": body["expectedRevision"] = Guid.Empty; break;
            case "amount": body["plannedAmount"] = "20"; break;
            case "confirmedOn": body["confirmedOn"] = "9999-12-31"; break;
            case "pair": body["plannedAmount"] = null; break;
            case "notes": body["notes"] = "bad\0notes"; break;
        }
        await Problem(await _client.PutAsJsonAsync(EntryPath(d, plan) + "/amount", body), 400);
        Assert.Equivalent(plan, await ReadAsync(d, plan.Id));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("[]")]
    public async Task MalformedBodiesAre400AcrossCaptureAndEveryMutation(string text)
    {
        var d = await SeedAsync(); var plan = await CaptureAsync(d);
        await Problem(await _client.PostAsync(d.Root, Json(text)), 400);
        await Problem(await _client.PutAsync(PlanPath(d, plan) + "/header", Json(text)), 400);
        await Problem(await _client.PostAsync(PlanPath(d, plan) + "/entries", Json(text)), 400);
        foreach (var suffix in new[] { "", "/amount", "/metadata" })
            await Problem(await _client.PutAsync(EntryPath(d, plan) + suffix, Json(text)), 400);
        await Problem(await _client.PostAsync(EntryPath(d, plan) + "/remove", Json(text)), 400);
        Assert.Equivalent(plan, await ReadAsync(d, plan.Id));
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=1.5")]
    [InlineData("?before=invalid")]
    [InlineData("?before=0001-01-01")]
    [InlineData("?before=9999-12-31")]
    [InlineData("/copy-source")]
    [InlineData("/copy-source?effectiveFrom=invalid")]
    [InlineData("/copy-source?effectiveFrom=0001-01-01")]
    [InlineData("/effective?asOf=invalid")]
    [InlineData("/effective?asOf=9999-12-31")]
    public async Task InvalidHistoryCopyAndAsOfQueryValuesAre400(string suffix)
    {
        var d = await SeedAsync();
        await Problem(await _client.GetAsync(d.Root + suffix), 400);
        await Problem(await _client.GetAsync(d.Outgoings + "?asOf=0001-01-01"), 400);
        await Problem(await _client.GetAsync(d.Outgoings + "?includeRetired=invalid"), 400);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndForeignSnapshotEntryItemAndMetadataReferencesAre404(bool foreign)
    {
        var d = await SeedAsync(); var other = await SeedAsync();
        var own = await CaptureAsync(d); var outside = await CaptureAsync(other);
        var snapshotId = foreign ? outside.Id : Guid.NewGuid();
        var entryId = foreign ? outside.Entries[0].Id : Guid.NewGuid();
        var itemId = foreign ? other.Energy.Id : Guid.NewGuid();
        var categoryId = foreign ? other.Category.Id : Guid.NewGuid();
        var merchantId = foreign ? other.Merchant.Id : Guid.NewGuid();
        var path = d.Root + $"/{snapshotId}";
        await Problem(await _client.GetAsync(path), 404);
        await Problem(await _client.PutAsJsonAsync(path + "/header", new { expectedRevision = own.Revision, effectiveFrom = January, notes = (string?)null }), 404);
        await Problem(await _client.PostAsJsonAsync(path + "/entries", new { expectedRevision = own.Revision, entry = Entry(d.Food.Id) }), 404);
        foreach (var target in new[] { path + $"/entries/{own.Entries[0].Id}", PlanPath(d, own) + $"/entries/{entryId}" })
        {
            await Problem(await _client.PutAsJsonAsync(target, new { expectedRevision = own.Revision, entry = Entry(d.Energy.Id) }), 404);
            await Problem(await _client.PutAsJsonAsync(target + "/amount", new { expectedRevision = own.Revision, plannedAmount = 20, confirmedOn = CheckedOn, notes = (string?)null }), 404);
            await Problem(await _client.PutAsJsonAsync(target + "/metadata", new { expectedRevision = own.Revision, metadata = Metadata(own.Entries[0]) }), 404);
            await Problem(await _client.PostAsJsonAsync(target + "/remove", new { expectedRevision = own.Revision }), 404);
        }
        var body = CreateBody(d, "2026-02-01"); body["entries"] = new[] { Entry(itemId) };
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 404);
        await Problem(await _client.PostAsJsonAsync(PlanPath(d, own) + "/entries", new { expectedRevision = own.Revision, entry = Entry(itemId) }), 404);
        body = CreateBody(d, "2026-02-01"); body["copiedFromBudgetSnapshotId"] = snapshotId;
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 404);
        var source = Entry(d.Energy.Id, "Carry"); source["source"] = new { snapshotId, entryId };
        body = CreateBody(d, "2026-02-01"); body["entries"] = new[] { source };
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 404);
        source["source"] = new { snapshotId = own.Id, entryId };
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 404);
        foreach (var field in new[] { "expenseCategoryId", "merchantId" })
        {
            var metadata = Metadata(own.Entries[0]); metadata[field] = field == "expenseCategoryId" ? categoryId : merchantId;
            var entry = Entry(d.Energy.Id); entry["metadata"] = metadata;
            body = CreateBody(d, "2026-02-01"); body["entries"] = new[] { entry };
            await Problem(await _client.PostAsJsonAsync(d.Root, body), 404);
            await Problem(await _client.PutAsJsonAsync(EntryPath(d, own) + "/metadata", new { expectedRevision = own.Revision, metadata }), 404);
        }
        body = CreateBody(d, "2026-02-01", outside.Id);
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 409);
        Assert.Equivalent(own, await ReadAsync(d, own.Id));
        Assert.Equivalent(outside, await ReadAsync(other, outside.Id));
    }

    [Fact]
    public async Task MissingHouseholdsDoNotBecomeEmptyInventoriesOrUnknownBudgetResponses()
    {
        var d = await SeedAsync(); var unknown = Path(Guid.NewGuid());
        foreach (var suffix in new[] { "", $"/{Guid.NewGuid()}", "/effective?asOf=" + January, "/copy-source?effectiveFrom=" + January })
            await Problem(await _client.GetAsync(unknown + suffix), 404);
        await Problem(await _client.GetAsync($"/api/households/{Guid.NewGuid()}/budgets/current-outgoings"), 404);
        await Problem(await _client.PostAsJsonAsync(unknown, CreateBody(d)), 404);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("amount")]
    [InlineData("metadata")]
    [InlineData("entry")]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task StaleRevisionsAre409ForEveryMutationAndDoNotOverwriteCurrentState(string kind)
    {
        var d = await SeedAsync(); var first = await CaptureAsync(d);
        var current = await Saved(await _client.PutAsJsonAsync(PlanPath(d, first) + "/header",
            new { expectedRevision = first.Revision, effectiveFrom = January, notes = "Winner" }));
        HttpResponseMessage response = kind switch
        {
            "header" => await _client.PutAsJsonAsync(PlanPath(d, first) + "/header", new { expectedRevision = first.Revision, effectiveFrom = January, notes = "Stale" }),
            "amount" => await _client.PutAsJsonAsync(EntryPath(d, first) + "/amount", new { expectedRevision = first.Revision, plannedAmount = 20, confirmedOn = CheckedOn, notes = (string?)null }),
            "metadata" => await _client.PutAsJsonAsync(EntryPath(d, first) + "/metadata", new { expectedRevision = first.Revision, metadata = Metadata(first.Entries[0]) }),
            "entry" => await _client.PutAsJsonAsync(EntryPath(d, first), new { expectedRevision = first.Revision, entry = Entry(d.Energy.Id) }),
            "add" => await _client.PostAsJsonAsync(PlanPath(d, first) + "/entries", new { expectedRevision = first.Revision, entry = Entry(d.Food.Id) }),
            _ => await _client.PostAsJsonAsync(EntryPath(d, first) + "/remove", new { expectedRevision = first.Revision })
        };
        var problem = await Problem(response, 409);
        Assert.Equal("Budget snapshot revision conflict", problem.GetProperty("title").GetString());
        Assert.Equivalent(current, await ReadAsync(d, first.Id));
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("frequency")]
    [InlineData("interval")]
    public async Task MetadataOnlyEditsRejectMeaningChangesWithoutReconfirmingOrRecopying(string kind)
    {
        var d = await SeedAsync(); var source = await CaptureAsync(d); var child = await CarryAsync(d, source);
        var metadata = Metadata(child.Entries[0]);
        metadata[kind == "currency" ? "currencyCode" : kind == "frequency" ? "frequency" : "intervalCount"] =
            kind == "currency" ? "USD" : kind == "frequency" ? "Year" : 3;
        await Problem(await _client.PutAsJsonAsync(EntryPath(d, child) + "/metadata", new { expectedRevision = child.Revision, metadata }), 400);
        Assert.Equivalent(child, await ReadAsync(d, child.Id));
        var explicitValue = Entry(d.Energy.Id, amount: 20); explicitValue["metadata"] = metadata;
        var corrected = await Saved(await _client.PutAsJsonAsync(EntryPath(d, child), new { expectedRevision = child.Revision, entry = explicitValue }));
        Assert.Equal(20, corrected.Entries[0].PlannedAmount);
        Assert.Equal(BudgetValueOrigin.Confirmed, corrected.Entries[0].ValueOrigin);
        Assert.Null(corrected.Entries[0].CarriedFromBudgetEntryId);
    }

    [Fact]
    public async Task RetiredItemsRemainHistoricalChoicesAndFilteringDoesNotChangePlanCompleteness()
    {
        var d = await SeedAsync();
        (await _client.PutAsJsonAsync($"/api/households/{d.Home}/budgets/items/{d.Energy.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        var body = CreateBody(d); body["entries"] = new[] { Entry(d.Energy.Id, "Unrecorded") };
        var plan = await CaptureAsync(d, body);
        var visible = (await _client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>(d.Outgoings + "?asOf=" + January + "&includeRetired=false"))!;
        Assert.Single(visible.Items); Assert.Equal(d.Food.Id, visible.Items[0].Item.Id);
        Assert.False(visible.IsComplete); Assert.Equal(plan.Id, visible.SnapshotId);
        var copied = await CarryAsync(d, plan);
        Assert.Equal(BudgetValueOrigin.Unrecorded, copied.Entries[0].ValueOrigin);
        Assert.Null(copied.Entries[0].LastConfirmedDate);
        Assert.Null(copied.Entries[0].CarriedFromBudgetEntryId);
    }

    [Fact]
    public async Task HistoricalCurrencySurvivesCarryAndMetadataEditsButCannotBeNewlySelected()
    {
        var d = await SeedAsync(); var source = await CaptureAsync(d);
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "BudgetEntries" SET "CurrencyCode" = {"ZZZ"} WHERE "BudgetSnapshotId" = {source.Id}""");
        var child = await CarryAsync(d, source);
        Assert.Equal("ZZZ", child.Entries[0].CurrencyCode);
        var metadata = Metadata(child.Entries[0]); metadata["itemName"] = "Historical label";
        child = await Saved(await _client.PutAsJsonAsync(EntryPath(d, child) + "/metadata", new { expectedRevision = child.Revision, metadata }));
        Assert.Equal("ZZZ", child.Entries[0].CurrencyCode);
        var body = CreateBody(d, "2026-03-01"); var entry = Entry(d.Energy.Id); entry["metadata"] = metadata; body["entries"] = new[] { entry };
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        metadata["currencyCode"] = "YYY";
        await Problem(await _client.PutAsJsonAsync(EntryPath(d, child) + "/metadata", new { expectedRevision = child.Revision, metadata }), 400);
    }

    [Fact]
    public async Task FailedSourceEntryDeletionRollsBackHttpSourceAndDescendantResponses()
    {
        var d = await SeedAsync(); var source = await CaptureAsync(d); var child = await CarryAsync(d, source);
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION "RejectApiBudgetDelete"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Synthetic delete failure'; END; $$;
                CREATE TRIGGER "RejectApiBudgetDelete" BEFORE DELETE ON "BudgetEntries"
                FOR EACH ROW EXECUTE FUNCTION "RejectApiBudgetDelete"();
                """);
        var problem = await Problem(await _client.PostAsJsonAsync(EntryPath(d, source) + "/remove", new { expectedRevision = source.Revision }), 500);
        Assert.DoesNotContain("Synthetic", problem.GetProperty("detail").GetString()!);
        Assert.Equivalent(source, await ReadAsync(d, source.Id));
        Assert.Equivalent(child, await ReadAsync(d, child.Id));
    }

    [Theory]
    [InlineData("same")]
    [InlineData("payload")]
    [InlineData("date")]
    public async Task ConcurrentHttpCreatesHave201AndReplayOrBusinessConflictOutcomes(string kind)
    {
        var d = await SeedAsync();
        var barrier = new SaveBarrier();
        await using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextFactory<HearthcastDbContext>>();
            services.AddSingleton<IDbContextFactory<HearthcastDbContext>>(new BarrierFactory(_database, barrier));
        }));
        using var client = app.CreateClient(new() { HandleCookies = false });
        var id = Guid.NewGuid(); var first = CreateBody(d, id: id);
        var second = CreateBody(d, id: kind == "date" ? Guid.NewGuid() : id);
        if (kind == "payload") second["notes"] = "Different request";
        var results = await Task.WhenAll(client.PostAsJsonAsync(d.Root, first), client.PostAsJsonAsync(d.Root, second));
        var created = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.NotNull(created.Headers.Location);
        if (kind == "same")
        {
            var replay = Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Null(replay.Headers.Location);
            Assert.Equivalent(await created.Content.ReadFromJsonAsync<BudgetSnapshotResponse>(), await replay.Content.ReadFromJsonAsync<BudgetSnapshotResponse>());
            replay.Dispose();
        }
        else await Problem(Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict), 409);
        created.Dispose();
        Assert.Single((await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root))!.Records);
    }

    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        private int _reads;
        internal int Reads => Volatile.Read(ref _reads);
        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref _reads);
            return instant;
        }
    }

    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _arrivals);
            if (count == 2) _ready.TrySetResult();
            if (count <= 2) await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }
    private sealed class BarrierFactory : IDbContextFactory<HearthcastDbContext>
    {
        private readonly DbContextOptions<HearthcastDbContext> _options;
        internal BarrierFactory(PostgresTestDatabase database, SaveBarrier barrier)
        {
            using var context = database.CreateDbContext();
            _options = new DbContextOptionsBuilder<HearthcastDbContext>().UseNpgsql(context.Database.GetConnectionString()).AddInterceptors(barrier).Options;
        }
        public HearthcastDbContext CreateDbContext() => new(_options);
    }

    private async Task InvalidEntryEverywhere(Data d, BudgetSnapshotResponse plan, Dictionary<string, object?> entry)
    {
        var body = CreateBody(d, "2026-02-01"); body["entries"] = new[] { entry };
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        await Problem(await _client.PostAsJsonAsync(PlanPath(d, plan) + "/entries", new { expectedRevision = plan.Revision, entry }), 400);
        await Problem(await _client.PutAsJsonAsync(EntryPath(d, plan), new { expectedRevision = plan.Revision, entry }), 400);
        Assert.Equivalent(plan, await ReadAsync(d, plan.Id));
        Assert.Single((await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(d.Root))!.Records);
    }
    private async Task InvalidMetadataEverywhere(Data d, BudgetSnapshotResponse plan, Dictionary<string, object?> metadata)
    {
        var entry = Entry(d.Energy.Id); entry["metadata"] = metadata;
        await InvalidEntryEverywhere(d, plan, entry);
        await Problem(await _client.PutAsJsonAsync(EntryPath(d, plan) + "/metadata", new { expectedRevision = plan.Revision, metadata }), 400);
        Assert.Equivalent(plan, await ReadAsync(d, plan.Id));
    }

    private static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Problem(HttpResponseMessage response, int status)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(status, problem.GetProperty("status").GetInt32());
            Assert.True(problem.TryGetProperty("traceId", out _));
            return problem;
        }
    }
}
