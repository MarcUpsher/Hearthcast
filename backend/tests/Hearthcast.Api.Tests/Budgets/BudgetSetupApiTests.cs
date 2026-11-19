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

public sealed class BudgetSetupApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ApiTestFactory _factory = null!;
    private HttpClient _client = null!;
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
    private sealed record Data(Guid Home, ExpenseCategoryResponse Category, MerchantResponse Merchant, BudgetItemResponse Item)
    {
        internal string Root => Path(Home);
        internal string ItemPath => Root + $"/{Item.Id}";
    }
    private static string Path(Guid home) => $"/api/households/{home}/budgets/items";
    private async Task<Guid> HomeAsync()
    {
        using var response = await _client.PostAsJsonAsync("/api/households", new { name = "Home", firstPersonDisplayName = "Alex" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<HouseholdResponse>())!.Id;
    }
    private async Task<Data> SeedAsync()
    {
        var home = await HomeAsync();
        var spending = $"/api/households/{home}/spending";
        using var categoryResponse = await _client.PostAsJsonAsync(spending + "/categories", new { name = "Bills" });
        using var merchantResponse = await _client.PostAsJsonAsync(spending + "/merchants", new { name = "Supplier" });
        categoryResponse.EnsureSuccessStatusCode(); merchantResponse.EnsureSuccessStatusCode();
        var category = (await categoryResponse.Content.ReadFromJsonAsync<ExpenseCategoryResponse>())!;
        var merchant = (await merchantResponse.Content.ReadFromJsonAsync<MerchantResponse>())!;
        var body = new { expenseCategoryId = category.Id, code = " energy ", name = " Energy ", merchantId = merchant.Id,
            currencyCode = " gbp ", frequency = "mOnTh", intervalCount = 1, notes = " Usage " };
        var item = await CreateAsync(Path(home), body);
        return new(home, category, merchant, item);
    }
    private async Task<BudgetItemResponse> CreateAsync(string path, object body)
    {
        using var response = await _client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var saved = (await response.Content.ReadFromJsonAsync<BudgetItemResponse>())!;
        Assert.Equal(path + $"/{saved.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(saved, await _client.GetFromJsonAsync<BudgetItemResponse>(response.Headers.Location));
        return saved;
    }
    private static Dictionary<string, object?> Body(Data d, bool update = false)
    {
        var body = new Dictionary<string, object?>
        {
            ["expenseCategoryId"] = d.Category.Id, ["name"] = "Changed", ["merchantId"] = d.Merchant.Id,
            ["currencyCode"] = "USD", ["frequency"] = "Year", ["intervalCount"] = 3, ["notes"] = "Changed notes"
        };
        if (!update) body["code"] = "NEW";
        return body;
    }

    [Fact]
    public async Task HttpWorkflowCreatesEditsRetiresAndRestoresStableItemsWithNamedFrequencyAndNoAmount()
    {
        var d = await SeedAsync();
        Assert.Equal("ENERGY", d.Item.Code);
        Assert.Equal("Energy", d.Item.Name);
        Assert.Equal("GBP", d.Item.CurrencyCode);
        Assert.Equal("Usage", d.Item.Notes);
        Assert.Equal(BudgetFrequency.Month, d.Item.Frequency);
        var wire = await _client.GetFromJsonAsync<JsonElement>(d.ItemPath);
        Assert.Equal("Month", wire.GetProperty("frequency").GetString());
        Assert.False(wire.TryGetProperty("plannedAmount", out _));
        Assert.False(wire.TryGetProperty("revision", out _));
        using var update = await _client.PutAsJsonAsync(d.ItemPath + "/details", Body(d, true));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var edited = (await update.Content.ReadFromJsonAsync<BudgetItemResponse>())!;
        Assert.Equal(d.Item.Id, edited.Id);
        Assert.Equal(d.Item.Code, edited.Code);
        Assert.Equal("USD", edited.CurrencyCode);
        Assert.Equal(BudgetFrequency.Year, edited.Frequency);
        Assert.Equal(3, edited.IntervalCount);
        Assert.Equal(edited, await _client.GetFromJsonAsync<BudgetItemResponse>(d.ItemPath));
        foreach (var flag in new[] { true, false })
        {
            using var response = await _client.PutAsJsonAsync(d.ItemPath + "/retirement", new { isRetired = flag });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            edited = edited with { IsRetired = flag };
            Assert.Equal(edited, await response.Content.ReadFromJsonAsync<BudgetItemResponse>());
        }
        await Problem(await _client.DeleteAsync(d.ItemPath), 405);
    }

    [Fact]
    public async Task CreationDefaultsOptionalFieldsAndAcceptsMaximumCodeAndRecurrenceBounds()
    {
        var d = await SeedAsync();
        var optional = await CreateAsync(d.Root, new { expenseCategoryId = d.Category.Id, code = "FOOD", name = "Food", currencyCode = "JPY", frequency = " week " });
        Assert.Equal(1, optional.IntervalCount);
        Assert.Null(optional.MerchantId); Assert.Null(optional.Notes);
        Assert.Equal(BudgetFrequency.Week, optional.Frequency);
        var max = await CreateAsync(d.Root, new { expenseCategoryId = d.Category.Id, code = new string('a', 64), name = "Other", currencyCode = "GBP", frequency = "Year", intervalCount = int.MaxValue });
        Assert.Equal(new string('A', 64), max.Code);
        Assert.Equal(int.MaxValue, max.IntervalCount);
    }

    [Fact]
    public async Task InventoriesAreOrderedScopedAndFilterOnlyItemRetirement()
    {
        var d = await SeedAsync();
        var other = await SeedAsync();
        var first = await CreateAsync(d.Root, new { expenseCategoryId = d.Category.Id, code = "BILLS", name = "Bills", currencyCode = "GBP", frequency = "Month" });
        var next = await CreateAsync(d.Root, new { expenseCategoryId = d.Category.Id, code = "ENERGY-2", name = "Energy", currencyCode = "GBP", frequency = "Month" });
        (await _client.PutAsJsonAsync($"/api/households/{d.Home}/spending/categories/{d.Category.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync($"/api/households/{d.Home}/spending/merchants/{d.Merchant.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        Assert.Equal(new[] { first.Id, d.Item.Id, next.Id }, (await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root + "?includeRetired=false"))!.Select(i => i.Id));
        (await _client.PutAsJsonAsync(d.ItemPath + "/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        Assert.Equal(3, (await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!.Length);
        Assert.Equal(new[] { first.Id, next.Id }, (await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root + "?includeRetired=false"))!.Select(i => i.Id));
        Assert.Equal(other.Item.Id, Assert.Single((await _client.GetFromJsonAsync<BudgetItemResponse[]>(other.Root))!).Id);
        await Problem(await _client.GetAsync(d.Root + "?includeRetired=invalid"), 400);
    }

    [Fact]
    public async Task ExplicitNullClearsMerchantAndNotesWhileAcceptedFullEditsUseLastWriterWins()
    {
        var d = await SeedAsync();
        var body = Body(d, true);
        body["merchantId"] = null; body["notes"] = null;
        using var cleared = await _client.PutAsJsonAsync(d.ItemPath + "/details", body);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var item = (await cleared.Content.ReadFromJsonAsync<BudgetItemResponse>())!;
        Assert.Null(item.MerchantId); Assert.Null(item.Notes);
        // There is no setup revision token: an accepted later edit replaces the submitted defaults.
        body["name"] = "Later edit"; body["merchantId"] = d.Merchant.Id; body["notes"] = " \u2000 ";
        using var later = await _client.PutAsJsonAsync(d.ItemPath + "/details", body);
        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        var saved = (await later.Content.ReadFromJsonAsync<BudgetItemResponse>())!;
        Assert.Equal("Later edit", saved.Name); Assert.Null(saved.Notes);
        Assert.Equal(d.Merchant.Id, saved.MerchantId);
    }

    [Theory]
    [InlineData("expenseCategoryId")]
    [InlineData("name")]
    [InlineData("merchantId")]
    [InlineData("currencyCode")]
    [InlineData("frequency")]
    [InlineData("intervalCount")]
    [InlineData("notes")]
    public async Task ReplacementEditsRequireEveryFieldIncludingExplicitNullableOnes(string missing)
    {
        var d = await SeedAsync();
        var body = Body(d, true); body.Remove(missing);
        await Problem(await _client.PutAsJsonAsync(d.ItemPath + "/details", body), 400);
        Assert.Equal(d.Item, await _client.GetFromJsonAsync<BudgetItemResponse>(d.ItemPath));
    }

    [Theory]
    [InlineData("expenseCategoryId")]
    [InlineData("code")]
    [InlineData("name")]
    [InlineData("currencyCode")]
    [InlineData("frequency")]
    public async Task CreateRequiresNamedFieldsRatherThanInferredDefaults(string missing)
    {
        var d = await SeedAsync();
        var body = Body(d); body.Remove(missing);
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        Assert.Single((await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!);
    }

    [Theory]
    [InlineData("expenseCategoryId", null)]
    [InlineData("expenseCategoryId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("merchantId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("name", null)]
    [InlineData("name", " \u00a0\u2000 ")]
    [InlineData("name", "bad\0name")]
    [InlineData("currencyCode", null)]
    [InlineData("currencyCode", "ZZZ")]
    [InlineData("currencyCode", "GB1")]
    [InlineData("intervalCount", null)]
    [InlineData("intervalCount", 0)]
    [InlineData("intervalCount", -1)]
    [InlineData("intervalCount", 1.5)]
    [InlineData("intervalCount", "2")]
    [InlineData("notes", "bad\0notes")]
    public async Task InvalidCreateAndUpdateValuesReturn400AndLeaveAllSavedDefaultsUnchanged(string field, object? value)
    {
        var d = await SeedAsync();
        var create = Body(d); create[field] = value;
        var update = Body(d, true); update[field] = value;
        await Problem(await _client.PostAsJsonAsync(d.Root, create), 400);
        await Problem(await _client.PutAsJsonAsync(d.ItemPath + "/details", update), 400);
        Assert.Equal(d.Item, Assert.Single((await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData("2")]
    [InlineData("Week, Month")]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task FrequencyRequiresOneDefinedTextLabelAndNeverParsesNumbersOrCombinedLabels(object? frequency)
    {
        var d = await SeedAsync();
        var create = Body(d); create["frequency"] = frequency;
        var update = Body(d, true); update["frequency"] = frequency;
        await Problem(await _client.PostAsJsonAsync(d.Root, create), 400);
        await Problem(await _client.PutAsJsonAsync(d.ItemPath + "/details", update), 400);
        Assert.Equal(d.Item, Assert.Single((await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!));
    }

    [Theory]
    [InlineData("code")]
    [InlineData("id")]
    [InlineData("householdId")]
    [InlineData("plannedAmount")]
    [InlineData("lastOutgoingAmount")]
    [InlineData("expectedRevision")]
    public async Task UnknownIdentityAmountAndRevisionFieldsAreRejectedOnUpdates(string field)
    {
        var d = await SeedAsync();
        var body = Body(d, true); body[field] = "unexpected";
        await Problem(await _client.PutAsJsonAsync(d.ItemPath + "/details", body), 400);
        if (field != "code")
        {
            body = Body(d); body[field] = "unexpected";
            await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        }
        Assert.Equal(d.Item, Assert.Single((await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"isRetired\":null}")]
    [InlineData("{\"isRetired\":\"true\"}")]
    [InlineData("{\"isRetired\":true,\"notes\":\"unexpected\"}")]
    public async Task MalformedBodiesAndRetirementRequestsCannotChangeSavedSetup(string body)
    {
        var d = await SeedAsync();
        await Problem(await _client.PostAsync(d.Root, Json(body)), 400);
        await Problem(await _client.PutAsync(d.ItemPath + "/details", Json(body)), 400);
        await Problem(await _client.PutAsync(d.ItemPath + "/retirement", Json(body)), 400);
        Assert.Equal(d.Item, Assert.Single((await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!));
    }

    [Theory]
    [InlineData("bad code")]
    [InlineData("-CODE")]
    [InlineData("")]
    public async Task InvalidItemCodesCannotBeCreated(string code)
    {
        var d = await SeedAsync();
        var body = Body(d); body["code"] = code;
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        body["code"] = new string('a', 65);
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        Assert.Single((await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignAndMissingReferencesReturn404ForAllOperationsWithoutChangingEitherHousehold(bool foreign)
    {
        var d = await SeedAsync(); var other = await SeedAsync();
        var itemId = foreign ? other.Item.Id : Guid.NewGuid();
        var categoryId = foreign ? other.Category.Id : Guid.NewGuid();
        var merchantId = foreign ? other.Merchant.Id : Guid.NewGuid();
        var path = d.Root + $"/{itemId}";
        await Problem(await _client.GetAsync(path), 404);
        await Problem(await _client.PutAsJsonAsync(path + "/details", Body(d, true)), 404);
        await Problem(await _client.PutAsJsonAsync(path + "/retirement", new { isRetired = true }), 404);
        foreach (var update in new[] { false, true })
        {
            var endpoint = update ? d.ItemPath + "/details" : d.Root;
            foreach (var field in new[] { "expenseCategoryId", "merchantId" })
            {
                var body = Body(d, update); body[field] = field == "merchantId" ? merchantId : categoryId;
                await Problem(update ? await _client.PutAsJsonAsync(endpoint, body) : await _client.PostAsJsonAsync(endpoint, body), 404);
            }
        }
        Assert.Equal(d.Item, await _client.GetFromJsonAsync<BudgetItemResponse>(d.ItemPath));
        Assert.Equal(other.Item, await _client.GetFromJsonAsync<BudgetItemResponse>(other.ItemPath));
    }

    [Fact]
    public async Task EmptyHouseholdsHaveEmptyListsWhileUnknownHouseholdsReturn404()
    {
        var home = await HomeAsync();
        Assert.Empty((await _client.GetFromJsonAsync<BudgetItemResponse[]>(Path(home)))!);
        var d = await SeedAsync(); var unknown = Path(Guid.NewGuid());
        await Problem(await _client.GetAsync(unknown), 404);
        await Problem(await _client.PostAsJsonAsync(unknown, Body(d)), 404);
        await Problem(await _client.GetAsync(unknown + $"/{d.Item.Id}"), 404);
        await Problem(await _client.PutAsJsonAsync(unknown + $"/{d.Item.Id}/details", Body(d, true)), 404);
        await Problem(await _client.PutAsJsonAsync(unknown + $"/{d.Item.Id}/retirement", new { isRetired = true }), 404);
    }

    [Fact]
    public async Task RetiredReferencesRemainUsableAndDuplicateCodesRemainReserved()
    {
        var d = await SeedAsync();
        (await _client.PutAsJsonAsync($"/api/households/{d.Home}/spending/categories/{d.Category.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync($"/api/households/{d.Home}/spending/merchants/{d.Merchant.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync(d.ItemPath + "/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        var body = Body(d); body["code"] = " eNeRgY ";
        var problem = await Problem(await _client.PostAsJsonAsync(d.Root, body), 409);
        Assert.Equal("Operation conflicts with current state", problem.GetProperty("title").GetString());
        body["code"] = "OLD";
        await CreateAsync(d.Root, body);
        using var update = await _client.PutAsJsonAsync(d.ItemPath + "/details", Body(d, true));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.True((await update.Content.ReadFromJsonAsync<BudgetItemResponse>())!.IsRetired);
    }

    [Fact]
    public async Task SavedPlanContextAndRevisionAreUnaffectedByHttpSetupEdits()
    {
        var d = await SeedAsync();
        var snapshots = new BudgetSnapshotService(_database);
        var original = (await snapshots.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 1, 1),
            [new(d.Item.Id, BudgetEntryValueMode.Confirm, 200, new(2025, 12, 28))]))).Snapshot;
        using var edit = await _client.PutAsJsonAsync(d.ItemPath + "/details", Body(d, true));
        edit.EnsureSuccessStatusCode();
        Assert.Equivalent(original, await snapshots.GetAsync(d.Home, original.Id));
        var amounts = await snapshots.GetCurrentOutgoingsAsync(d.Home, new(2026, 1, 1));
        var current = Assert.Single(amounts.Items);
        Assert.Equal("USD", current.Item.CurrencyCode);
        Assert.Equal("GBP", current.Entry!.CurrencyCode);
        Assert.Equal(200, current.Entry.PlannedAmount);
    }

    [Fact]
    public async Task HistoricCurrencyCanBeRetainedOnUpdateButNotSelectedForNewItemsOrOtherCodes()
    {
        var d = await SeedAsync();
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "BudgetItems" SET "CurrencyCode" = {"ZZZ"} WHERE "Id" = {d.Item.Id}""");
        var body = Body(d, true); body["currencyCode"] = " zzz ";
        using var update = await _client.PutAsJsonAsync(d.ItemPath + "/details", body);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = (await update.Content.ReadFromJsonAsync<BudgetItemResponse>())!;
        Assert.Equal("ZZZ", saved.CurrencyCode);
        body = Body(d); body["currencyCode"] = "ZZZ";
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        body = Body(d, true); body["currencyCode"] = "YYY";
        await Problem(await _client.PutAsJsonAsync(d.ItemPath + "/details", body), 400);
        Assert.Equal(saved, await _client.GetFromJsonAsync<BudgetItemResponse>(d.ItemPath));
    }

    [Fact]
    public async Task ConcurrentDuplicateHttpCreatesReturnOne201AndOne409()
    {
        var d = await SeedAsync();
        var barrier = new SaveBarrier();
        await using var app = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextFactory<HearthcastDbContext>>();
            services.AddSingleton<IDbContextFactory<HearthcastDbContext>>(new BarrierFactory(_database, barrier));
        }));
        using var client = app.CreateClient(new() { HandleCookies = false });
        var results = await Task.WhenAll(client.PostAsJsonAsync(d.Root, Body(d)), client.PostAsJsonAsync(d.Root, Body(d)));
        var created = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.NotNull(created.Headers.Location);
        await Problem(Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict), 409);
        created.Dispose();
        Assert.Equal(2, (await _client.GetFromJsonAsync<BudgetItemResponse[]>(d.Root))!.Length);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
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
    [Fact]
    public async Task PaymentDatesRoundTripAndOmittedDatePreservesWhileExplicitNullClears()
    {
        var d = await SeedAsync(); var create = Body(d); create["paymentAnchorDate"] = "2026-11-19";
        var item = await CreateAsync(d.Root, create); var path = d.Root + $"/{item.Id}";
        Assert.Equal(new DateOnly(2026, 11, 19), item.PaymentAnchorDate);
        var wire = await _client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("2026-11-19", wire.GetProperty("paymentAnchorDate").GetString());
        var body = Body(d, true);
        using var older = await _client.PutAsJsonAsync(path + "/details", body); older.EnsureSuccessStatusCode();
        Assert.Equal(item.PaymentAnchorDate, (await older.Content.ReadFromJsonAsync<BudgetItemResponse>())!.PaymentAnchorDate);
        body["paymentAnchorDate"] = "2024-02-29";
        using var edit = await _client.PutAsJsonAsync(path + "/details", body); edit.EnsureSuccessStatusCode();
        Assert.Equal(new DateOnly(2024, 2, 29), (await edit.Content.ReadFromJsonAsync<BudgetItemResponse>())!.PaymentAnchorDate);
        using var retire = await _client.PutAsJsonAsync(path + "/retirement", new { isRetired = true }); retire.EnsureSuccessStatusCode();
        Assert.Equal(new DateOnly(2024, 2, 29), (await retire.Content.ReadFromJsonAsync<BudgetItemResponse>())!.PaymentAnchorDate);
        body["paymentAnchorDate"] = null;
        using var clear = await _client.PutAsJsonAsync(path + "/details", body); clear.EnsureSuccessStatusCode();
        Assert.Null((await clear.Content.ReadFromJsonAsync<BudgetItemResponse>())!.PaymentAnchorDate);
        Assert.Null(d.Item.PaymentAnchorDate);
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    [InlineData("2026-02-30")]
    [InlineData("2026-11-19T00:00:00Z")]
    [InlineData("")]
    [InlineData("not a date")]
    public async Task InvalidPaymentDatesFailWithoutChangingSetup(string date)
    {
        var d = await SeedAsync(); var body = Body(d); body["paymentAnchorDate"] = date;
        await Problem(await _client.PostAsJsonAsync(d.Root, body), 400);
        body.Remove("code"); await Problem(await _client.PutAsJsonAsync(d.ItemPath + "/details", body), 400);
        Assert.Equal(d.Item, await _client.GetFromJsonAsync<BudgetItemResponse>(d.ItemPath));
    }

    [Fact]
    public async Task PaymentDateUpdatesCannotCrossHouseholds()
    {
        var d = await SeedAsync(); var foreign = await SeedAsync(); var body = Body(d, true); body["paymentAnchorDate"] = "2026-11-25";
        await Problem(await _client.PutAsJsonAsync(d.Root + $"/{foreign.Item.Id}/details", body), 404);
        Assert.Equal(foreign.Item, await _client.GetFromJsonAsync<BudgetItemResponse>(foreign.ItemPath));
    }

}
