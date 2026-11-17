using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hearthcast.Api.Households.Contracts;
using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Api.Tests.Spending;

public sealed class SpendingSetupApiTests : IAsyncLifetime
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

    private sealed record Setup(Guid Home, ExpenseCategoryResponse Category, MerchantResponse Merchant,
        SpendingItemResponse Item, UsualPriceResponse Price)
    {
        public string Root => Path(Home);
    }

    private async Task<Guid> CreateHomeAsync()
    {
        using var result = await _client.PostAsJsonAsync("/api/households", new { name = "Home", firstPersonDisplayName = "Alex" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var home = (await result.Content.ReadFromJsonAsync<HouseholdResponse>())!;
        var read = (await _client.GetFromJsonAsync<HouseholdResponse>(result.Headers.Location))!;
        Assert.Equal(home.Id, read.Id);
        Assert.Equal(home.People.ToArray(), read.People.ToArray());
        return home.Id;
    }

    private async Task<Setup> SeedAsync()
    {
        var home = await CreateHomeAsync();
        var root = Path(home);
        var category = await CreateAsync<ExpenseCategoryResponse>(root + "/categories", new { name = "\t Food\n" });
        var merchant = await CreateAsync<MerchantResponse>(root + "/merchants", new { name = " Shop " });
        var item = await CreateAsync<SpendingItemResponse>(root + "/items", new
        {
            expenseCategoryId = category.Id, code = " eggs-6 ", name = " Eggs ", unitKey = " PACK-6 ", unitLabel = "Six eggs", notes = " Notes "
        });
        var price = await CreateAsync<UsualPriceResponse>(root + "/usual-prices", new
        {
            spendingItemId = item.Id, merchantId = merchant.Id, currencyCode = " gbp ", usualUnitPrice = 3.123456m
        });
        return new(home, category, merchant, item, price);
    }

    private async Task<T> CreateAsync<T>(string path, object body)
    {
        using var result = await _client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.NotNull(result.Headers.Location);
        Assert.StartsWith(path + "/", result.Headers.Location!.AbsolutePath);
        var saved = (await result.Content.ReadFromJsonAsync<T>())!;
        Assert.Equal(saved, await _client.GetFromJsonAsync<T>(result.Headers.Location));
        return saved;
    }

    [Fact]
    public async Task SetupWorkflowCreatesReadsEditsRetiresAndRestoresThroughHttp()
    {
        var d = await SeedAsync();
        Assert.Equal("Food", d.Category.Name);
        Assert.Equal("Shop", d.Merchant.Name);
        Assert.Equal("EGGS-6", d.Item.Code);
        Assert.Equal("pack-6", d.Item.UnitKey);
        Assert.Equal("Notes", d.Item.Notes);
        Assert.Equal("GBP", d.Price.CurrencyCode);
        Assert.Equal(3.123456m, d.Price.UsualUnitPrice);
        var category = await CreateAsync<ExpenseCategoryResponse>(d.Root + "/categories", new { name = "Groceries" });
        var categoryUpdate = await _client.PutAsJsonAsync(d.Root + $"/categories/{d.Category.Id}/name", new { name = " Other food " });
        Assert.Equal(d.Category with { Name = "Other food" }, await categoryUpdate.Content.ReadFromJsonAsync<ExpenseCategoryResponse>());
        var merchantUpdate = await _client.PutAsJsonAsync(d.Root + $"/merchants/{d.Merchant.Id}/name", new { name = " Renamed shop " });
        Assert.Equal(d.Merchant with { Name = "Renamed shop" }, await merchantUpdate.Content.ReadFromJsonAsync<MerchantResponse>());
        var itemPath = d.Root + $"/items/{d.Item.Id}";
        var details = await _client.PutAsJsonAsync(itemPath + "/details", new
        {
            expenseCategoryId = category.Id, name = " Large eggs ", unitLabel = " Box of six ", notes = (string?)null
        });
        var item = d.Item with { Name = "Large eggs", ExpenseCategoryId = category.Id, UnitLabel = "Box of six", Notes = null };
        Assert.Equal(item, await details.Content.ReadFromJsonAsync<SpendingItemResponse>());
        Assert.Equal(item, await _client.GetFromJsonAsync<SpendingItemResponse>(itemPath));
        Assert.Equal(d.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(d.Root + $"/usual-prices/{d.Price.Id}"));
        foreach (var resource in new[] { $"categories/{d.Category.Id}", $"merchants/{d.Merchant.Id}", $"items/{d.Item.Id}" })
        {
            var retired = await _client.PutAsJsonAsync(d.Root + "/" + resource + "/retirement", new { isRetired = true });
            Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
            Assert.True((await retired.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isRetired").GetBoolean());
            var restored = await _client.PutAsJsonAsync(d.Root + "/" + resource + "/retirement", new { isRetired = false });
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            Assert.False((await restored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isRetired").GetBoolean());
            await Problem(await _client.DeleteAsync(d.Root + "/" + resource), 405);
        }
        await Problem(await _client.DeleteAsync(d.Root + $"/usual-prices/{d.Price.Id}"), 405);
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.Categories.ToArrayAsync()); // Independent of asset/debt categories.
        Assert.Empty(await db.TrackedItems.ToArrayAsync());
    }

    [Fact]
    public async Task InventoriesAreOrderedScopedAndFilterOnlyTheirOwnRetirementFlag()
    {
        var d = await SeedAsync();
        await SeedAsync();
        var earlyCategory = await CreateAsync<ExpenseCategoryResponse>(d.Root + "/categories", new { name = "A category" });
        var earlyMerchant = await CreateAsync<MerchantResponse>(d.Root + "/merchants", new { name = "A shop" });
        var item = await CreateAsync<SpendingItemResponse>(d.Root + "/items", new
        {
            expenseCategoryId = earlyCategory.Id, code = "A", name = "A product", unitKey = "each", unitLabel = "Each"
        });
        Assert.Equal(new[] { earlyCategory.Id, d.Category.Id }, (await _client.GetFromJsonAsync<ExpenseCategoryResponse[]>(d.Root + "/categories"))!.Select(c => c.Id));
        Assert.Equal(new[] { earlyMerchant.Id, d.Merchant.Id }, (await _client.GetFromJsonAsync<MerchantResponse[]>(d.Root + "/merchants"))!.Select(m => m.Id));
        Assert.Equal(new[] { item.Id, d.Item.Id }, (await _client.GetFromJsonAsync<SpendingItemResponse[]>(d.Root + "/items"))!.Select(i => i.Id));
        (await _client.PutAsJsonAsync(d.Root + $"/categories/{earlyCategory.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync(d.Root + $"/merchants/{earlyMerchant.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        Assert.Equal(2, (await _client.GetFromJsonAsync<SpendingItemResponse[]>(d.Root + "/items?includeRetired=false"))!.Length);
        (await _client.PutAsJsonAsync(d.Root + $"/items/{item.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        Assert.Equal(d.Category.Id, Assert.Single((await _client.GetFromJsonAsync<ExpenseCategoryResponse[]>(d.Root + "/categories?includeRetired=false"))!).Id);
        Assert.Equal(d.Merchant.Id, Assert.Single((await _client.GetFromJsonAsync<MerchantResponse[]>(d.Root + "/merchants?includeRetired=false"))!).Id);
        Assert.Equal(d.Item.Id, Assert.Single((await _client.GetFromJsonAsync<SpendingItemResponse[]>(d.Root + "/items?includeRetired=false"))!).Id);
        Assert.Equal(2, (await _client.GetFromJsonAsync<SpendingItemResponse[]>(d.Root + "/items"))!.Length);
        await Problem(await _client.GetAsync(d.Root + "/items?includeRetired=invalid"), 400);
    }

    [Fact]
    public async Task ExactPriceLookupReturns204ForUnrecordedCombinationsAndNeverFallsBack()
    {
        var d = await SeedAsync();
        string Lookup(Guid item, Guid merchant, string currency) => d.Root + $"/items/{item}/usual-price?merchantId={merchant}&currencyCode={currency}";
        Assert.Equal(d.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(Lookup(d.Item.Id, d.Merchant.Id, "gbp")));
        using var absentCurrency = await _client.GetAsync(Lookup(d.Item.Id, d.Merchant.Id, "ZAR"));
        Assert.Equal(HttpStatusCode.NoContent, absentCurrency.StatusCode);
        Assert.Empty(await absentCurrency.Content.ReadAsStringAsync());
        var merchant = await CreateAsync<MerchantResponse>(d.Root + "/merchants", new { name = "Other shop" });
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync(Lookup(d.Item.Id, merchant.Id, "GBP"))).StatusCode);
        var item = await CreateAsync<SpendingItemResponse>(d.Root + "/items", new
        {
            expenseCategoryId = d.Category.Id, code = "EGGS-12", name = "Eggs", unitKey = "pack-12", unitLabel = "Twelve eggs"
        });
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync(Lookup(item.Id, d.Merchant.Id, "GBP"))).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<UsualPriceResponse[]>(d.Root + $"/items/{item.Id}/usual-prices"))!);
        var zar = await CreateAsync<UsualPriceResponse>(d.Root + "/usual-prices", new
        {
            spendingItemId = d.Item.Id, merchantId = d.Merchant.Id, currencyCode = "ZAR", usualUnitPrice = 50m
        });
        Assert.Equal(new[] { d.Price, zar }, await _client.GetFromJsonAsync<UsualPriceResponse[]>(d.Root + $"/items/{d.Item.Id}/usual-prices"));
        (await _client.PutAsJsonAsync(d.Root + $"/items/{d.Item.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        (await _client.PutAsJsonAsync(d.Root + $"/merchants/{d.Merchant.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        Assert.Equal(d.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(Lookup(d.Item.Id, d.Merchant.Id, "GBP")));
    }

    [Fact]
    public async Task PriceUpdatesRequireRevisionAndExplicitReconfirmationAdvancesIt()
    {
        var d = await SeedAsync();
        var path = d.Root + $"/usual-prices/{d.Price.Id}";
        using var updated = await _client.PutAsJsonAsync(path, new { expectedRevision = d.Price.Revision, usualUnitPrice = 0m });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var first = (await updated.Content.ReadFromJsonAsync<UsualPriceResponse>())!;
        Assert.Equal(0m, first.UsualUnitPrice);
        Assert.NotEqual(d.Price.Revision, first.Revision);
        Assert.Equal(first, await _client.GetFromJsonAsync<UsualPriceResponse>(path));
        var problem = await Problem(await _client.PutAsJsonAsync(path, new { expectedRevision = d.Price.Revision, usualUnitPrice = 2m }), 409);
        Assert.Equal("Usual price revision conflict", problem.GetProperty("title").GetString());
        using var confirmed = await _client.PutAsJsonAsync(path, new { expectedRevision = first.Revision, usualUnitPrice = 0m });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var second = (await confirmed.Content.ReadFromJsonAsync<UsualPriceResponse>())!;
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.Equal(0m, second.UsualUnitPrice);
        Assert.Equal(second, await _client.GetFromJsonAsync<UsualPriceResponse>(path));
    }

    [Fact]
    public async Task DuplicateItemCodesAndPricesReturn409WithoutOverwritingData()
    {
        var d = await SeedAsync();
        (await _client.PutAsJsonAsync(d.Root + $"/items/{d.Item.Id}/retirement", new { isRetired = true })).EnsureSuccessStatusCode();
        await Problem(await _client.PostAsJsonAsync(d.Root + "/items", new
        {
            expenseCategoryId = d.Category.Id, code = "eGgS-6", name = "Other", unitKey = "each", unitLabel = "Each"
        }), 409);
        await Problem(await _client.PostAsJsonAsync(d.Root + "/usual-prices", new
        {
            spendingItemId = d.Item.Id, merchantId = d.Merchant.Id, currencyCode = "gBp", usualUnitPrice = 1m
        }), 409);
        Assert.Equal(d.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(d.Root + $"/usual-prices/{d.Price.Id}"));
        Assert.Equal(d.Item.Id, Assert.Single((await _client.GetFromJsonAsync<SpendingItemResponse[]>(d.Root + "/items"))!).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrMissingIdsReturn404ForAllResourceOperations(bool foreign)
    {
        var d = await SeedAsync();
        var other = await SeedAsync();
        var c = foreign ? other.Category.Id : Guid.NewGuid();
        var m = foreign ? other.Merchant.Id : Guid.NewGuid();
        var i = foreign ? other.Item.Id : Guid.NewGuid();
        var p = foreign ? other.Price.Id : Guid.NewGuid();
        foreach (var resource in new[] { $"categories/{c}", $"merchants/{m}", $"items/{i}", $"usual-prices/{p}" })
            await Problem(await _client.GetAsync(d.Root + "/" + resource), 404);
        await Problem(await _client.PutAsJsonAsync(d.Root + $"/categories/{c}/name", new { name = "New" }), 404);
        await Problem(await _client.PutAsJsonAsync(d.Root + $"/merchants/{m}/name", new { name = "New" }), 404);
        foreach (var resource in new[] { $"categories/{c}", $"merchants/{m}", $"items/{i}" })
            await Problem(await _client.PutAsJsonAsync(d.Root + "/" + resource + "/retirement", new { isRetired = true }), 404);
        await Problem(await _client.PutAsJsonAsync(d.Root + $"/items/{i}/details", new
        {
            expenseCategoryId = d.Category.Id, name = "New", unitLabel = "Each", notes = (string?)null
        }), 404);
        await Problem(await _client.PutAsJsonAsync(d.Root + $"/items/{d.Item.Id}/details", new
        {
            expenseCategoryId = c, name = "New", unitLabel = "Each", notes = (string?)null
        }), 404);
        await Problem(await _client.PostAsJsonAsync(d.Root + "/items", new
        {
            expenseCategoryId = c, code = "NEW", name = "New", unitKey = "each", unitLabel = "Each"
        }), 404);
        await Problem(await _client.PostAsJsonAsync(d.Root + "/usual-prices", new { spendingItemId = i, merchantId = d.Merchant.Id, currencyCode = "GBP", usualUnitPrice = 2m }), 404);
        await Problem(await _client.PostAsJsonAsync(d.Root + "/usual-prices", new { spendingItemId = d.Item.Id, merchantId = m, currencyCode = "GBP", usualUnitPrice = 2m }), 404);
        await Problem(await _client.PutAsJsonAsync(d.Root + $"/usual-prices/{p}", new { expectedRevision = other.Price.Revision, usualUnitPrice = 2m }), 404);
        await Problem(await _client.GetAsync(d.Root + $"/items/{i}/usual-prices"), 404);
        await Problem(await _client.GetAsync(d.Root + $"/items/{i}/usual-price?merchantId={d.Merchant.Id}&currencyCode=GBP"), 404);
        await Problem(await _client.GetAsync(d.Root + $"/items/{d.Item.Id}/usual-price?merchantId={m}&currencyCode=GBP"), 404);
        Assert.Equal(d.Item, await _client.GetFromJsonAsync<SpendingItemResponse>(d.Root + $"/items/{d.Item.Id}"));
        Assert.Equal(other.Item, await _client.GetFromJsonAsync<SpendingItemResponse>(other.Root + $"/items/{other.Item.Id}"));
        Assert.Equal(other.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(other.Root + $"/usual-prices/{other.Price.Id}"));
    }

    [Fact]
    public async Task ExistingEmptyHouseholdsHaveEmptyListsButMissingHouseholdsReturn404()
    {
        var home = await CreateHomeAsync();
        foreach (var resource in new[] { "categories", "merchants", "items" })
        {
            var response = await _client.GetAsync(Path(home) + "/" + resource);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
            await Problem(await _client.GetAsync(Path(Guid.NewGuid()) + "/" + resource), 404);
        }
        await Problem(await _client.PostAsJsonAsync(Path(Guid.NewGuid()) + "/categories", new { name = "Food" }), 404);
        await Problem(await _client.PostAsJsonAsync(Path(Guid.NewGuid()) + "/merchants", new { name = "Shop" }), 404);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":42}")]
    [InlineData("{\"name\":\" \\t \"}")]
    [InlineData("{\"name\":\"Bad\\u0000name\"}")]
    [InlineData("{\"name\":\"New\",\"householdId\":\"ignored\"}")]
    public async Task InvalidNameBodiesAreRejectedWithoutChangingOrCreatingSetup(string body)
    {
        var d = await SeedAsync();
        foreach (var resource in new[] { "categories", "merchants" })
            await Problem(await _client.PostAsync(d.Root + "/" + resource, Json(body)), 400);
        await Problem(await _client.PutAsync(d.Root + $"/categories/{d.Category.Id}/name", Json(body)), 400);
        await Problem(await _client.PutAsync(d.Root + $"/merchants/{d.Merchant.Id}/name", Json(body)), 400);
        Assert.Equal(d.Category, Assert.Single((await _client.GetFromJsonAsync<ExpenseCategoryResponse[]>(d.Root + "/categories"))!));
        Assert.Equal(d.Merchant, Assert.Single((await _client.GetFromJsonAsync<MerchantResponse[]>(d.Root + "/merchants"))!));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"isRetired\":null}")]
    [InlineData("{\"isRetired\":\"yes\"}")]
    [InlineData("{\"isRetired\":true,\"name\":\"New\"}")]
    public async Task RetirementRequiresAnExplicitBooleanAndRejectsExtraFields(string body)
    {
        var d = await SeedAsync();
        foreach (var resource in new[] { $"categories/{d.Category.Id}", $"merchants/{d.Merchant.Id}", $"items/{d.Item.Id}" })
        {
            await Problem(await _client.PutAsync(d.Root + "/" + resource + "/retirement", Json(body)), 400);
            var saved = await _client.GetFromJsonAsync<JsonElement>(d.Root + "/" + resource);
            Assert.False(saved.GetProperty("isRetired").GetBoolean());
        }
    }

    [Theory]
    [InlineData("code", "bad code")]
    [InlineData("code", "_bad")]
    [InlineData("code", "bad\0code")]
    [InlineData("code", "overlong")]
    [InlineData("unitKey", "bad unit")]
    [InlineData("unitKey", "overlong")]
    [InlineData("unitLabel", " ")]
    [InlineData("notes", "bad\0notes")]
    public async Task InvalidItemFieldsAre400AndLeaveInventoryUnchanged(string field, string value)
    {
        var d = await SeedAsync();
        var body = new Dictionary<string, object?>
        {
            ["expenseCategoryId"] = d.Category.Id, ["code"] = "NEW", ["name"] = "New",
            ["unitKey"] = "each", ["unitLabel"] = "Each"
        };
        body[field] = value == "overlong" ? new string('a', field == "code" ? 65 : 33) : value;
        await Problem(await _client.PostAsJsonAsync(d.Root + "/items", body), 400);
        Assert.Equal(d.Item, Assert.Single((await _client.GetFromJsonAsync<SpendingItemResponse[]>(d.Root + "/items"))!));
    }

    [Fact]
    public async Task ItemBoundaryLengthsAndWhitespaceNormalizeThroughHttp()
    {
        var d = await SeedAsync();
        var item = await CreateAsync<SpendingItemResponse>(d.Root + "/items", new
        {
            expenseCategoryId = d.Category.Id, code = " " + new string('a', 64) + " ", name = "\u00a0New\u3000",
            unitKey = "\t" + new string('b', 32) + "\n", unitLabel = " Each ", notes = "\t\n"
        });
        Assert.Equal(new string('A', 64), item.Code);
        Assert.Equal(new string('b', 32), item.UnitKey);
        Assert.Equal("New", item.Name);
        Assert.Null(item.Notes);
    }

    [Theory]
    [InlineData("missingNotes")]
    [InlineData("code")]
    [InlineData("unitKey")]
    [InlineData("isRetired")]
    [InlineData("unitLabel")]
    [InlineData("notes")]
    public async Task InvalidItemReplacementIsAtomicAndCannotChangeImmutableFields(string error)
    {
        var d = await SeedAsync();
        var body = new Dictionary<string, object?>
        {
            ["expenseCategoryId"] = d.Category.Id, ["name"] = "Changed", ["unitLabel"] = "Changed label", ["notes"] = null
        };
        if (error == "missingNotes") body.Remove("notes");
        else body[error] = error == "isRetired" ? true : error == "unitLabel" ? " " : error == "notes" ? "Bad\0notes" : "Changed";
        await Problem(await _client.PutAsJsonAsync(d.Root + $"/items/{d.Item.Id}/details", body), 400);
        Assert.Equal(d.Item, await _client.GetFromJsonAsync<SpendingItemResponse>(d.Root + $"/items/{d.Item.Id}"));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000000000")]
    [InlineData("0.0000001")]
    [InlineData("1e100")]
    [InlineData("null")]
    [InlineData("\"NaN\"")]
    public async Task InvalidPricesAre400ForCreateAndUpdateWithoutChangingTheRevision(string number)
    {
        var d = await SeedAsync();
        var create = $"{{\"spendingItemId\":\"{d.Item.Id}\",\"merchantId\":\"{d.Merchant.Id}\",\"currencyCode\":\"ZAR\",\"usualUnitPrice\":{number}}}";
        var update = $"{{\"expectedRevision\":\"{d.Price.Revision}\",\"usualUnitPrice\":{number}}}";
        await Problem(await _client.PostAsync(d.Root + "/usual-prices", Json(create)), 400);
        await Problem(await _client.PutAsync(d.Root + $"/usual-prices/{d.Price.Id}", Json(update)), 400);
        Assert.Equal(d.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(d.Root + $"/usual-prices/{d.Price.Id}"));
        Assert.Single((await _client.GetFromJsonAsync<UsualPriceResponse[]>(d.Root + $"/items/{d.Item.Id}/usual-prices"))!);
    }

    [Theory]
    [InlineData("currencyCode", "GB")]
    [InlineData("currencyCode", "GB1")]
    [InlineData("currencyCode", "G B")]
    [InlineData("spendingItemId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("merchantId", "00000000-0000-0000-0000-000000000000")]
    public async Task InvalidPriceIdentifiersOrCurrencyFailValidation(string field, string value)
    {
        var d = await SeedAsync();
        var body = new Dictionary<string, object?>
        {
            ["spendingItemId"] = d.Item.Id, ["merchantId"] = d.Merchant.Id, ["currencyCode"] = "GBP", ["usualUnitPrice"] = 2m
        };
        body[field] = value;
        await Problem(await _client.PostAsJsonAsync(d.Root + "/usual-prices", body), 400);
        Assert.Equal(d.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(d.Root + $"/usual-prices/{d.Price.Id}"));
    }

    [Fact]
    public async Task PriceRevisionAndIdentityChangesRequireExplicitValidContracts()
    {
        var d = await SeedAsync();
        var path = d.Root + $"/usual-prices/{d.Price.Id}";
        foreach (var body in new[] { "{}", "{\"usualUnitPrice\":2}", "{\"expectedRevision\":\"00000000-0000-0000-0000-000000000000\",\"usualUnitPrice\":2}",
            $"{{\"expectedRevision\":\"{d.Price.Revision}\",\"usualUnitPrice\":2,\"currencyCode\":\"ZAR\"}}" })
            await Problem(await _client.PutAsync(path, Json(body)), 400);
        Assert.Equal(d.Price, await _client.GetFromJsonAsync<UsualPriceResponse>(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?merchantId=invalid&currencyCode=GBP")]
    [InlineData("?merchantId=00000000-0000-0000-0000-000000000000&currencyCode=GBP")]
    [InlineData("?currencyCode=GBP")]
    public async Task LookupRequiresAValidMerchantAndCurrency(string query)
    {
        var d = await SeedAsync();
        await Problem(await _client.GetAsync(d.Root + $"/items/{d.Item.Id}/usual-price" + query), 400);
        await Problem(await _client.GetAsync(d.Root + $"/items/{d.Item.Id}/usual-price?merchantId={d.Merchant.Id}&currencyCode=GB1"), 400);
    }

    [Fact]
    public async Task RequiredFieldsProduceNamedValidationErrorsRatherThanDefaultValues()
    {
        var home = await CreateHomeAsync();
        var root = Path(home);
        var itemProblem = await Problem(await _client.PostAsync(root + "/items", Json("{}")), 400);
        foreach (var field in new[] { "ExpenseCategoryId", "Code", "Name", "UnitKey", "UnitLabel" }) Field(itemProblem, field);
        var priceProblem = await Problem(await _client.PostAsync(root + "/usual-prices", Json("{}")), 400);
        foreach (var field in new[] { "SpendingItemId", "MerchantId", "CurrencyCode", "UsualUnitPrice" }) Field(priceProblem, field);
        Field(await Problem(await _client.PostAsync(root + "/categories", Json("{}")), 400), "Name");
        await using var db = _database.CreateDbContext();
        Assert.Empty(await db.SpendingItems.ToArrayAsync());
        Assert.Empty(await db.SpendingItemMerchantPrices.ToArrayAsync());
    }

    private static string Path(Guid household) => $"/api/households/{household}/spending";
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static void Field(JsonElement problem, string name) =>
        Assert.NotEmpty(problem.GetProperty("errors").GetProperty(name).EnumerateArray());

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
