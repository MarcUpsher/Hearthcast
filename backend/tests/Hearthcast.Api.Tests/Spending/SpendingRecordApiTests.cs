using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hearthcast.Api.Tests.Spending;

public sealed class SpendingRecordApiTests : IAsyncLifetime
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

    private sealed record Data(Guid Home, Merchant Merchant, ExpenseCategory Category, SpendingItem Item)
    {
        internal string Root => $"/api/households/{Home}/spending";
        internal string Records => Root + "/records";
    }
    private async Task<Data> SeedAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var setup = new SpendingSetupService(_database);
        var merchant = await setup.CreateMerchantAsync(home.Id, "Shop");
        var category = await setup.CreateCategoryAsync(home.Id, "Food");
        var item = await setup.CreateItemAsync(home.Id, category.Id, "EGGS", "Eggs", "pack-6", "Six eggs");
        return new(home.Id, merchant, category, item);
    }
    private static Dictionary<string, object?> Line(Data d, bool detailed = false) => detailed
        ? new() { ["expenseCategoryId"] = d.Category.Id, ["netAmount"] = 3m, ["spendingItemId"] = d.Item.Id, ["quantity"] = 1m, ["unitPrice"] = 3m }
        : new() { ["expenseCategoryId"] = d.Category.Id, ["netAmount"] = 10m };
    private static Dictionary<string, object?> Body(Data d, bool detailed = false) => new()
    {
        ["id"] = Guid.NewGuid(), ["merchantId"] = d.Merchant.Id, ["spendingDate"] = "2026-11-17",
        ["currencyCode"] = " gbp ", ["kind"] = "Payment", ["lines"] = new[] { Line(d, detailed) }
    };
    private async Task<SpendingRecordResponse> CreateAsync(Data d, object body)
    {
        using var result = await _client.PostAsJsonAsync(d.Records, body);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var record = (await result.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        Assert.EndsWith(d.Records + $"/{record.Id}", result.Headers.Location!.ToString());
        Equivalent(record, (await _client.GetFromJsonAsync<SpendingRecordResponse>(result.Headers.Location))!);
        return record;
    }
    private static void Equivalent(SpendingRecordResponse expected, SpendingRecordResponse actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    private static object Header(SpendingRecordResponse record, string? notes = null) => new
    {
        expectedRevision = record.Revision, spendingDate = record.SpendingDate,
        receiptTotal = (decimal?)null, externalReference = (string?)null, notes
    };
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Problem(HttpResponseMessage response, int status)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(status, json.GetProperty("status").GetInt32());
            Assert.True(json.TryGetProperty("traceId", out _));
            return json;
        }
    }

    [Fact]
    public async Task CreateReplayCorrectionAndVoidPreserveTheClientIdentityAndCurrentState()
    {
        var d = await SeedAsync(); var body = Body(d);
        var record = await CreateAsync(d, body);
        Assert.Equal((Guid)body["id"]!, record.Id);
        Assert.Equal(10m, record.TotalAmount);
        Assert.Equal(SpendingRecordKind.Payment, record.Kind);
        Assert.Null(record.Lines[0].SpendingItemId);
        Assert.Equal(PromotionStatus.Unknown, record.Lines[0].PromotionStatus);
        var replay = await _client.PostAsJsonAsync(d.Records, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Equivalent(record, (await replay.Content.ReadFromJsonAsync<SpendingRecordResponse>())!);
        var update = await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/header", Header(record, "Corrected"));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var corrected = (await update.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        Assert.NotEqual(record.Revision, corrected.Revision);
        var conflict = await Problem(await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/header", Header(record)), 409);
        Assert.Equal("Spending record revision conflict", conflict.GetProperty("title").GetString());
        var voidResponse = await _client.PostAsJsonAsync(d.Records + $"/{record.Id}/void", new { expectedRevision = corrected.Revision });
        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        var voided = (await voidResponse.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        Assert.True(voided.IsVoided);
        Assert.Equal(0m, voided.CountedAmount);
        Assert.Equal(10m, voided.TotalAmount);
        var afterVoid = await _client.PostAsJsonAsync(d.Records, body);
        Assert.Equal(HttpStatusCode.OK, afterVoid.StatusCode);
        Equivalent(voided, (await afterVoid.Content.ReadFromJsonAsync<SpendingRecordResponse>())!);
        Assert.Empty((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!.Records);
        Assert.Single((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records + "?includeVoided=true"))!.Records);
        await Problem(await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/header", Header(voided)), 409);
        await Problem(await _client.DeleteAsync(d.Records + $"/{record.Id}"), 405);
        body["notes"] = "Changed create payload";
        await Problem(await _client.PostAsJsonAsync(d.Records, body), 409);
        var raw = await _client.GetFromJsonAsync<JsonElement>(d.Records + $"/{record.Id}");
        Assert.False(raw.TryGetProperty("creationRequestHash", out _));
        Assert.Equal("Payment", raw.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ReplacementAndLineCorrectionAreAtomicWithExplicitPriceAdoption()
    {
        var d = await SeedAsync(); var body = Body(d, true);
        body["rememberPrices"] = new[] { new { linePosition = 1, chosenUnitPrice = 3m } };
        var record = await CreateAsync(d, body);
        var price = (await _client.GetFromJsonAsync<UsualPriceResponse[]>(d.Root + $"/items/{d.Item.Id}/usual-prices"))!.Single();
        var rejected = Body(d, true);
        rejected["rememberPrices"] = new[] { new { linePosition = 1, chosenUnitPrice = 2m, expectedRevision = Guid.NewGuid() } };
        await Problem(await _client.PostAsJsonAsync(d.Records, rejected), 409);
        await Problem(await _client.GetAsync(d.Records + $"/{rejected["id"]}"), 404);
        var replacement = await _client.PutAsJsonAsync(d.Records + $"/{record.Id}", new
        {
            expectedRevision = record.Revision, merchantId = d.Merchant.Id, spendingDate = record.SpendingDate,
            lines = new[] { Line(d, true), Line(d) }, receiptTotal = 13m, externalReference = (string?)null, notes = (string?)null,
            rememberPrices = new[] { new { linePosition = 1, chosenUnitPrice = 2m, expectedRevision = price.Revision } }
        });
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var changed = (await replacement.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        Assert.Equal(2, changed.Lines.Count);
        Assert.Equal("Reconciled", changed.ReconciliationStatus);
        Assert.Equal(13m, changed.TotalAmount);
        Assert.NotEqual(record.Lines[0].Id, changed.Lines[0].Id);
        price = (await _client.GetFromJsonAsync<UsualPriceResponse>(d.Root + $"/usual-prices/{price.Id}"))!;
        Assert.Equal(2m, price.UsualUnitPrice);
        var corrected = await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/lines/{changed.Lines[0].Id}", new
        {
            expectedRevision = changed.Revision, netAmount = 2m, quantity = 1m, unitPrice = 2m, promotionStatus = "Yes", notes = (string?)null,
            rememberPrices = new[] { new { linePosition = 1, chosenUnitPrice = 2m, expectedRevision = price.Revision } }
        });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        var last = (await corrected.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        Assert.Equal(12m, last.TotalAmount);
        Assert.Equal(PromotionStatus.Yes, last.Lines[0].PromotionStatus);
        Assert.Equal("Eggs", last.Lines[0].ItemName);
        await using var db = _database.CreateDbContext();
        Assert.Equal(2, await db.SpendingLines.CountAsync());
        Assert.Equal(last.Lines[0].Id, (await db.SpendingItemMerchantPrices.SingleAsync()).SourceSpendingLineId);
        (await _client.PostAsJsonAsync(d.Records + $"/{record.Id}/void", new { expectedRevision = last.Revision })).EnsureSuccessStatusCode();
        Assert.Null((await db.SpendingItemMerchantPrices.AsNoTracking().SingleAsync()).SourceSpendingLineId);
    }

    [Fact]
    public async Task RefundsCountNegativelyAndRetriesSurviveVoidingTheirOriginal()
    {
        var d = await SeedAsync(); var original = await CreateAsync(d, Body(d));
        var refundBody = Body(d); refundBody["kind"] = "Refund"; refundBody["originalPaymentId"] = original.Id;
        var refund = await CreateAsync(d, refundBody);
        Assert.Equal(-10m, refund.CountedAmount);
        Assert.Equal(original.Id, refund.OriginalPaymentId);
        Assert.False(refund.OriginalPaymentIsVoided);
        (await _client.PostAsJsonAsync(d.Records + $"/{original.Id}/void", new { expectedRevision = original.Revision })).EnsureSuccessStatusCode();
        var replay = await _client.PostAsJsonAsync(d.Records, refundBody);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Equivalent(refund with { OriginalPaymentIsVoided = true }, (await replay.Content.ReadFromJsonAsync<SpendingRecordResponse>())!);
        var detail = (await _client.GetFromJsonAsync<SpendingRecordResponse>(d.Records + $"/{refund.Id}"))!;
        Assert.True(detail.OriginalPaymentIsVoided);
        Assert.Equal(refund.Revision, detail.Revision);
        Assert.Equal(refund.CountedAmount, detail.CountedAmount);
        refundBody["id"] = Guid.NewGuid();
        await Problem(await _client.PostAsJsonAsync(d.Records, refundBody), 400);
    }

    [Fact]
    public async Task PagedHistoryReturnsCompleteReceiptsAndContinuesAcrossEqualDates()
    {
        var d = await SeedAsync(); var expected = new List<SpendingRecordResponse>();
        for (var i = 0; i < 5; i++)
        {
            var body = Body(d); body["lines"] = new[] { Line(d), Line(d), Line(d) };
            expected.Add(await CreateAsync(d, body));
        }
        var seen = new List<Guid>(); string? cursor = null;
        do
        {
            var page = (await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records + "?pageSize=2" + (cursor is null ? "" : "&cursor=" + cursor)))!;
            Assert.Equal(2, page.PageSize);
            Assert.InRange(page.Records.Count, 1, 2);
            Assert.All(page.Records, r => { Assert.Equal(3, r.Lines.Count); Assert.Equal(30m, r.TotalAmount); });
            seen.AddRange(page.Records.Select(r => r.Id)); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(expected.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).Select(r => r.Id), seen);
        var first = (await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records + "?pageSize=1"))!;
        await Problem(await _client.GetAsync(d.Records + "?pageSize=2&cursor=" + first.NextCursor), 400);
        var other = await SeedAsync();
        await Problem(await _client.GetAsync(other.Records + "?pageSize=1&cursor=" + first.NextCursor), 400);
        Assert.Empty((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records + "?from=2026-01-01&to=2026-01-31"))!.Records);
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=invalid")]
    [InlineData("?cursor=bad_cursor")]
    [InlineData("?from=2026-02-01&to=2026-01-01")]
    [InlineData("?includeVoided=invalid")]
    [InlineData("?from=not-a-date")]
    public async Task InvalidHistoryQueriesAre400(string query)
    {
        var d = await SeedAsync();
        await Problem(await _client.GetAsync(d.Records + query), 400);
    }

    [Fact]
    public async Task LastPaidAndCurrencyPolicyAreScopedAndDoNotInventDefaults()
    {
        var d = await SeedAsync();
        string Lookup(string currency = "GBP", string cutoff = "2026-11-17") => d.Root + $"/items/{d.Item.Id}/last-paid-price?merchantId={d.Merchant.Id}&currencyCode={currency}&onOrBefore={cutoff}";
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync(Lookup())).StatusCode);
        var body = Body(d, true); var line = Line(d, true); line["netAmount"] = 2.5m; line["promotionStatus"] = "Yes"; body["lines"] = new[] { line };
        var record = await CreateAsync(d, body);
        var price = (await _client.GetFromJsonAsync<LastPaidPriceResponse>(Lookup("gbp")))!;
        Assert.Equal(record.Id, price.SpendingRecordId);
        Assert.Equal(3m, price.UnitPrice); Assert.Equal(2.5m, price.EffectiveUnitPrice);
        Assert.Equal(PromotionStatus.Yes, price.PromotionStatus);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync(Lookup("ZZZ"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync(Lookup(cutoff: "2026-11-16"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.GetAsync(Lookup() + "&excludeRecordId=" + record.Id)).StatusCode);
        await Problem(await _client.GetAsync(d.Root + $"/items/{d.Item.Id}/last-paid-price"), 400);
        var currencies = (await _client.GetFromJsonAsync<SpendingCurrencyResponse[]>(d.Root + "/currencies"))!;
        Assert.Equal(2, currencies.Single(c => c.Code == "GBP").MinorUnits);
        Assert.Equal(0, currencies.Single(c => c.Code == "JPY").MinorUnits);
        Assert.Equal(3, currencies.Single(c => c.Code == "KWD").MinorUnits);
        Assert.DoesNotContain(currencies, c => c.Code == "ZZZ");
        await using var db = _database.CreateDbContext();
        Assert.Equal(0, await db.SpendingItemMerchantPrices.CountAsync());
        await Problem(await _client.GetAsync($"/api/households/{Guid.NewGuid()}/spending/currencies"), 404);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task MalformedOrMissingCreateBodiesAre400(string body)
    {
        var d = await SeedAsync();
        await Problem(await _client.PostAsync(d.Records, Json(body)), 400);
        Assert.Empty((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!.Records);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("merchantId")]
    [InlineData("spendingDate")]
    [InlineData("currencyCode")]
    [InlineData("kind")]
    [InlineData("lines")]
    public async Task CreateRequiresExplicitIdentityAndHeaderFields(string field)
    {
        var d = await SeedAsync(); var body = Body(d); body.Remove(field);
        var problem = await Problem(await _client.PostAsJsonAsync(d.Records, body), 400);
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Theory]
    [InlineData("missingAmount")]
    [InlineData("nullAmount")]
    [InlineData("negativeAmount")]
    [InlineData("precision")]
    [InlineData("pair")]
    [InlineData("emptyCategory")]
    [InlineData("emptyLines")]
    [InlineData("nullLine")]
    [InlineData("unknownField")]
    [InlineData("nullPriceChoice")]
    public async Task InvalidNestedValuesDoNotSilentlyBecomeDefaults(string error)
    {
        var d = await SeedAsync(); var body = Body(d); var line = Line(d);
        if (error == "missingAmount") line.Remove("netAmount");
        if (error == "nullAmount") line["netAmount"] = null;
        if (error == "negativeAmount") line["netAmount"] = -1m;
        if (error == "precision") line["netAmount"] = 0.001m;
        if (error == "pair") line["quantity"] = 1m;
        if (error == "emptyCategory") line["expenseCategoryId"] = Guid.Empty;
        if (error == "unknownField") line["itemName"] = "Untrusted name";
        body["lines"] = error == "emptyLines" ? Array.Empty<object>() : error == "nullLine" ? new object?[] { null } : new[] { line };
        if (error == "nullPriceChoice") body["rememberPrices"] = new object?[] { null };
        await Problem(await _client.PostAsJsonAsync(d.Records, body), 400);
        Assert.Empty((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!.Records);
    }

    [Fact]
    public async Task ExplicitZeroIsARecordedAmountAndUnknownCurrencyIsNotAssumed()
    {
        var d = await SeedAsync(); var body = Body(d); var line = Line(d); line["netAmount"] = 0m; body["lines"] = new[] { line };
        var record = await CreateAsync(d, body);
        Assert.Equal(0m, record.TotalAmount);
        body["id"] = Guid.NewGuid(); body["currencyCode"] = "ZZZ";
        await Problem(await _client.PostAsJsonAsync(d.Records, body), 400);
    }

    [Theory]
    [InlineData("kind", "1")]
    [InlineData("kind", "\"1\"")]
    [InlineData("kind", "\"Payment, Refund\"")]
    [InlineData("promotionStatus", "3")]
    [InlineData("promotionStatus", "\"1\"")]
    [InlineData("promotionStatus", "\"Unknown, Yes\"")]
    [InlineData("promotionStatus", "\"Other\"")]
    public async Task EnumsRequireASingleDefinedTextLabel(string field, string jsonValue)
    {
        var d = await SeedAsync(); var body = Body(d); var line = Line(d);
        using var value = JsonDocument.Parse(jsonValue);
        if (field == "kind") body[field] = value.RootElement.Clone();
        else { line[field] = value.RootElement.Clone(); body["lines"] = new[] { line }; }
        await Problem(await _client.PostAsJsonAsync(d.Records, body), 400);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndForeignResourcesAre404WithoutCrossHouseholdWrites(bool foreign)
    {
        var a = await SeedAsync(); var b = await SeedAsync();
        var own = await CreateAsync(a, Body(a)); var other = await CreateAsync(b, Body(b));
        var id = foreign ? other.Id : Guid.NewGuid(); var merchant = foreign ? b.Merchant.Id : Guid.NewGuid();
        var category = foreign ? b.Category.Id : Guid.NewGuid(); var item = foreign ? b.Item.Id : Guid.NewGuid();
        await Problem(await _client.GetAsync(a.Records + $"/{id}"), 404);
        await Problem(await _client.PutAsJsonAsync(a.Records + $"/{id}/header", Header(other)), 404);
        await Problem(await _client.PostAsJsonAsync(a.Records + $"/{id}/void", new { expectedRevision = other.Revision }), 404);
        var body = Body(a); body["merchantId"] = merchant;
        await Problem(await _client.PostAsJsonAsync(a.Records, body), 404);
        body = Body(a); body["lines"] = new[] { new { expenseCategoryId = category, netAmount = 1m } };
        await Problem(await _client.PostAsJsonAsync(a.Records, body), 404);
        body = Body(a); body["lines"] = new[] { new { expenseCategoryId = a.Category.Id, spendingItemId = item, netAmount = 1m } };
        await Problem(await _client.PostAsJsonAsync(a.Records, body), 404);
        body = Body(a); body["kind"] = "Refund"; body["originalPaymentId"] = id;
        await Problem(await _client.PostAsJsonAsync(a.Records, body), 404);
        await Problem(await _client.PutAsJsonAsync(a.Records + $"/{own.Id}/lines/{other.Lines[0].Id}", new
        {
            expectedRevision = own.Revision, netAmount = 1m, quantity = (decimal?)null, unitPrice = (decimal?)null,
            promotionStatus = "Unknown", notes = (string?)null
        }), 404);
        await Problem(await _client.GetAsync(a.Root + $"/items/{item}/last-paid-price?merchantId={a.Merchant.Id}&currencyCode=GBP&onOrBefore=2026-11-17"), 404);
        Equivalent(own, (await _client.GetFromJsonAsync<SpendingRecordResponse>(a.Records + $"/{own.Id}"))!);
        await Problem(await _client.GetAsync($"/api/households/{Guid.NewGuid()}/spending/records"), 404);
    }

    [Theory]
    [InlineData("currencyCode")]
    [InlineData("kind")]
    [InlineData("creationRequestHash")]
    public async Task ReplacementRejectsImmutableOrServerOwnedFields(string field)
    {
        var d = await SeedAsync(); var record = await CreateAsync(d, Body(d));
        var body = new Dictionary<string, object?>
        {
            ["expectedRevision"] = record.Revision, ["merchantId"] = d.Merchant.Id, ["spendingDate"] = "2026-11-17",
            ["lines"] = new[] { Line(d) }, ["receiptTotal"] = null, ["externalReference"] = null, ["notes"] = null,
            [field] = field == "kind" ? "Refund" : "Changed"
        };
        await Problem(await _client.PutAsJsonAsync(d.Records + $"/{record.Id}", body), 400);
        Equivalent(record, (await _client.GetFromJsonAsync<SpendingRecordResponse>(d.Records + $"/{record.Id}"))!);
    }

    [Fact]
    public async Task CorrectionsRequireRevisionAndExplicitNullableFields()
    {
        var d = await SeedAsync(); var record = await CreateAsync(d, Body(d));
        await Problem(await _client.PostAsJsonAsync(d.Records + $"/{record.Id}/void", new { }), 400);
        await Problem(await _client.PostAsJsonAsync(d.Records + $"/{record.Id}/void", new { expectedRevision = Guid.Empty }), 400);
        await Problem(await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/header", new { expectedRevision = record.Revision, spendingDate = record.SpendingDate }), 400);
        await Problem(await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/lines/{record.Lines[0].Id}", new { expectedRevision = record.Revision, netAmount = 0m }), 400);
        Equivalent(record, (await _client.GetFromJsonAsync<SpendingRecordResponse>(d.Records + $"/{record.Id}"))!);
    }

    [Theory]
    [InlineData("missingPosition")]
    [InlineData("missingAmount")]
    [InlineData("emptyRevision")]
    [InlineData("zeroPosition")]
    [InlineData("negativePosition")]
    [InlineData("absentPosition")]
    public async Task MalformedPriceChoicesAre400ForCreateAndCorrectionWithoutChangingRevisions(string error)
    {
        var d = await SeedAsync(); var body = Body(d, true);
        body["rememberPrices"] = new[] { new { linePosition = 1, chosenUnitPrice = 3m } };
        var record = await CreateAsync(d, body);
        var pricePath = d.Root + $"/items/{d.Item.Id}/usual-prices";
        var price = Assert.Single((await _client.GetFromJsonAsync<UsualPriceResponse[]>(pricePath))!);
        var choice = new Dictionary<string, object?>
        {
            ["linePosition"] = 1, ["chosenUnitPrice"] = 2m, ["expectedRevision"] = price.Revision
        };
        if (error == "missingPosition") choice.Remove("linePosition");
        if (error == "missingAmount") choice.Remove("chosenUnitPrice");
        if (error == "emptyRevision") choice["expectedRevision"] = Guid.Empty;
        if (error == "zeroPosition") choice["linePosition"] = 0;
        if (error == "negativePosition") choice["linePosition"] = -1;
        if (error == "absentPosition") choice["linePosition"] = 2;
        var rejectedCreate = Body(d, true); rejectedCreate["rememberPrices"] = new[] { choice };
        await Problem(await _client.PostAsJsonAsync(d.Records, rejectedCreate), 400);
        await Problem(await _client.GetAsync(d.Records + $"/{rejectedCreate["id"]}"), 404);
        await Problem(await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/lines/{record.Lines[0].Id}", new
        {
            expectedRevision = record.Revision, netAmount = 2m, quantity = 1m, unitPrice = 2m,
            promotionStatus = "Yes", notes = (string?)null, rememberPrices = new[] { choice }
        }), 400);
        Equivalent(record, (await _client.GetFromJsonAsync<SpendingRecordResponse>(d.Records + $"/{record.Id}"))!);
        Assert.Equal(price, Assert.Single((await _client.GetFromJsonAsync<UsualPriceResponse[]>(pricePath))!));
        Assert.Single((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!.Records);
    }

    [Fact]
    public async Task RefundPageResolvesOriginalVoidStateWithOneBatchedQuery()
    {
        var d = await SeedAsync();
        var original = await CreateAsync(d, Body(d)); var other = await CreateAsync(d, Body(d));
        var refunds = new List<SpendingRecordResponse>();
        foreach (var id in new[] { original.Id, other.Id, original.Id })
        {
            var body = Body(d); body["kind"] = "Refund"; body["originalPaymentId"] = id;
            refunds.Add(await CreateAsync(d, body));
        }
        (await _client.PostAsJsonAsync(d.Records + $"/{original.Id}/void", new { expectedRevision = original.Revision })).EnsureSuccessStatusCode();
        var capture = new OriginalStateQueryCapture();
        await using var alternate = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextFactory<HearthcastDbContext>>();
            services.AddSingleton<IDbContextFactory<HearthcastDbContext>>(new RecordingFactory(_database, capture));
        }));
        using var client = alternate.CreateClient(new() { HandleCookies = false });
        var page = (await client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!;
        Assert.Equal(1, capture.StateQueries);
        Assert.InRange(capture.TotalQueries, 2, 4); // No per-refund GET/lookup queries.
        foreach (var refund in refunds)
        {
            var saved = page.Records.Single(r => r.Id == refund.Id);
            Assert.Equal(refund.OriginalPaymentId == original.Id, saved.OriginalPaymentIsVoided);
            Assert.Equal(refund.Revision, saved.Revision);
            Assert.Equal(-10m, saved.CountedAmount);
        }
        Assert.Null(page.Records.Single(r => r.Id == other.Id).OriginalPaymentIsVoided);
    }

    private sealed class OriginalStateQueryCapture : DbCommandInterceptor
    {
        internal int TotalQueries { get; private set; }
        internal int StateQueries { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            TotalQueries++;
            if (command.CommandText.Contains("Spending original payment void states", StringComparison.Ordinal)) StateQueries++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RecordingFactory : IDbContextFactory<HearthcastDbContext>
    {
        private readonly DbContextOptions<HearthcastDbContext> _options;
        internal RecordingFactory(PostgresTestDatabase database, DbCommandInterceptor interceptor)
        {
            using var context = database.CreateDbContext();
            _options = new DbContextOptionsBuilder<HearthcastDbContext>().UseNpgsql(context.Database.GetConnectionString())
                .AddInterceptors(interceptor).Options;
        }
        public HearthcastDbContext CreateDbContext() => new(_options);
    }
}
