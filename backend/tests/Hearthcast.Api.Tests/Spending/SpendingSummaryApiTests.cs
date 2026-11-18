using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Households.Contracts;
using Hearthcast.Api.Spending.Contracts;
using Hearthcast.Api.Tests.Infrastructure;
using Hearthcast.Data.Tests.Infrastructure;

namespace Hearthcast.Api.Tests.Spending;

public sealed class SpendingSummaryApiTests : IAsyncLifetime
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

    private sealed record Data(Guid Home, ExpenseCategoryResponse Food, ExpenseCategoryResponse Dining,
        MerchantResponse Merchant, SpendingItemResponse Item)
    {
        internal string Root => $"/api/households/{Home}/spending";
    }

    private static string Path(Guid home, string query = "?from=2026-12-01&to=2026-12-31") =>
        $"/api/households/{home}/spending/summary" + query;

    private async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await _client.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Guid> HomeAsync() => (await PostAsync<HouseholdResponse>("/api/households",
        new { name = "Home", firstPersonDisplayName = "Alex" })).Id;

    private async Task<Data> SeedAsync()
    {
        var home = await HomeAsync();
        var root = $"/api/households/{home}/spending";
        var food = await PostAsync<ExpenseCategoryResponse>(root + "/categories", new { name = "Food" });
        var dining = await PostAsync<ExpenseCategoryResponse>(root + "/categories", new { name = "Eating Out" });
        var merchant = await PostAsync<MerchantResponse>(root + "/merchants", new { name = "Shop" });
        var item = await PostAsync<SpendingItemResponse>(root + "/items", new
        {
            expenseCategoryId = food.Id, code = "EGGS", name = "Eggs", unitKey = "pack-6", unitLabel = "Six eggs"
        });
        return new(home, food, dining, merchant, item);
    }

    private Task<SpendingRecordResponse> CaptureAsync(Data data, string date, object[] lines,
        string currency = "GBP", string kind = "Payment", Guid? original = null, decimal? receipt = null) =>
        PostAsync<SpendingRecordResponse>(data.Root + "/records", new
        {
            id = Guid.NewGuid(), merchantId = data.Merchant.Id, spendingDate = date, currencyCode = currency,
            kind, originalPaymentId = original, receiptTotal = receipt, lines
        });

    private static object Line(Guid category, decimal amount) => new { expenseCategoryId = category, netAmount = amount };

    private async Task VoidAsync(Data data, SpendingRecordResponse record)
    {
        using var response = await _client.PostAsJsonAsync(data.Root + $"/records/{record.Id}/void",
            new { expectedRevision = record.Revision });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<SpendingCategorySummaryResponse> ReportAsync(Guid home,
        string query = "?from=2026-12-01&to=2026-12-31")
    {
        using var response = await _client.GetAsync(Path(home, query));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<SpendingCategorySummaryResponse>())!;
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, int status)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(status, body.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
            return body;
        }
    }

    [Fact]
    public async Task CaptureToSummaryHttpWorkflowCombinesSavedAmountsWithRefundsCurrenciesAndReconciliation()
    {
        var data = await SeedAsync();
        var payment = await CaptureAsync(data, "2026-12-01", [Line(data.Food.Id, 25m)], receipt: 25m);
        var split = await CaptureAsync(data, "2026-12-10", [Line(data.Food.Id, 7m), Line(data.Dining.Id, 8m)], receipt: 20m);
        await CaptureAsync(data, "2026-12-31",
        [
            new { expenseCategoryId = data.Food.Id, netAmount = 3.25m, spendingItemId = data.Item.Id, quantity = 2m, unitPrice = 3m },
            new { expenseCategoryId = data.Food.Id, netAmount = 1.75m, spendingItemId = data.Item.Id, quantity = 1m, unitPrice = 3m }
        ], receipt: 5m);
        await CaptureAsync(data, "2026-12-31", [Line(data.Food.Id, 5m)], kind: "Refund", original: payment.Id, receipt: 5m);
        await CaptureAsync(data, "2026-12-15", [Line(data.Food.Id, 7m)], currency: "EUR");
        await CaptureAsync(data, "2026-11-30", [Line(data.Food.Id, 100m)], receipt: 200m);
        await CaptureAsync(data, "2027-01-01", [Line(data.Food.Id, 100m)], receipt: 200m);
        var voided = await CaptureAsync(data, "2026-12-20", [Line(data.Food.Id, 100m)], receipt: 200m);
        await VoidAsync(data, voided);

        var report = await ReportAsync(data.Home);

        Assert.Equal(data.Home, report.HouseholdId);
        Assert.Equal(new DateOnly(2026, 12, 1), report.From);
        Assert.Equal(new DateOnly(2026, 12, 31), report.To);
        Assert.Collection(report.Categories,
            row => Assert.Equal(new(data.Food.Id, "Food", "EUR", 2, 7m), row),
            row => Assert.Equal(new(data.Dining.Id, "Eating Out", "GBP", 2, 8m), row),
            row => Assert.Equal(new(data.Food.Id, "Food", "GBP", 2, 32m), row));
        Assert.Equal(new(1, 3, 1), report.ReconciliationRecordCounts);
        Assert.Equal(new SpendingReceiptWarningResponse(split.Id, new(2026, 12, 10), "Shop", "GBP", 20m, 15m, 5m),
            Assert.Single(report.Warnings));

        var wire = await _client.GetFromJsonAsync<JsonElement>(Path(data.Home));
        Assert.Equal(new[] { "categories", "from", "householdId", "reconciliationRecordCounts", "to", "warnings" },
            wire.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("2026-12-01", wire.GetProperty("from").GetString());
        Assert.Equal("2026-12-31", wire.GetProperty("to").GetString());
        var counts = wire.GetProperty("reconciliationRecordCounts");
        Assert.Equal(1, counts.GetProperty("notChecked").GetInt32());
        Assert.Equal(3, counts.GetProperty("reconciled").GetInt32());
        Assert.Equal(1, counts.GetProperty("unreconciled").GetInt32());
        Assert.Equal(JsonValueKind.Number, wire.GetProperty("categories")[0].GetProperty("netAmount").ValueKind);
        Assert.Equal(split.Id, wire.GetProperty("warnings")[0].GetProperty("spendingRecordId").GetGuid());
    }

    [Theory]
    [InlineData("", "From")]
    [InlineData("?from=2026-12-01", "To")]
    [InlineData("?to=2026-12-31", "From")]
    [InlineData("?from=&to=2026-12-31", "From")]
    [InlineData("?from=2026-12-01&to=", "To")]
    [InlineData("?from=December&to=2026-12-31", "From")]
    [InlineData("?from=2026-12-01&to=nope", "To")]
    [InlineData("?from=2026-02-30&to=2026-12-31", "From")]
    [InlineData("?from=2026-12-01&to=2026-13-01", "To")]
    [InlineData("?from=0001-01-01&to=2026-12-31", "From")]
    [InlineData("?from=2026-12-01&to=0001-01-01", "To")]
    [InlineData("?from=2026-12-31&to=2026-12-01", "From")]
    public async Task InvalidOrMissingRangeReturnsValidationProblemBeforeHouseholdLookup(string query, string field)
    {
        var body = await ProblemAsync(await _client.GetAsync(Path(Guid.NewGuid(), query)), 400);
        Assert.Contains(body.GetProperty("errors").EnumerateObject(),
            error => error.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownAndEmptyHouseholdIdentifiersReturnNotFoundProblem(bool empty)
    {
        var body = await ProblemAsync(await _client.GetAsync(Path(empty ? Guid.Empty : Guid.NewGuid())), 404);
        Assert.Equal("Resource not found", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task KnownHouseholdWithNoCapturedSpendingReturnsAnEmptyReportWithExplicitZeroCounts()
    {
        var home = await HomeAsync();

        var report = await ReportAsync(home);

        Assert.Equal(home, report.HouseholdId);
        Assert.Empty(report.Categories);
        Assert.Empty(report.Warnings);
        Assert.Equal(new(0, 0, 0), report.ReconciliationRecordCounts);
    }

    [Fact]
    public async Task ReportExcludesOtherHouseholdAmountsAndReceiptWarnings()
    {
        var selected = await SeedAsync();
        var other = await SeedAsync();
        await CaptureAsync(selected, "2026-12-10", [Line(selected.Food.Id, 5m)], receipt: 5m);
        await CaptureAsync(other, "2026-12-10", [Line(other.Food.Id, 100m)], receipt: 200m);

        var report = await ReportAsync(selected.Home);

        Assert.Equal(new(selected.Food.Id, "Food", "GBP", 2, 5m), Assert.Single(report.Categories));
        Assert.Empty(report.Warnings);
        Assert.Equal(new(0, 1, 0), report.ReconciliationRecordCounts);
    }

    [Fact]
    public async Task SingleDayAndArbitraryCrossYearRangesAreAcceptedWithoutCalendarPresets()
    {
        var data = await SeedAsync();
        await CaptureAsync(data, "2026-12-27", [Line(data.Food.Id, 100m)]);
        await CaptureAsync(data, "2026-12-28", [Line(data.Food.Id, 1m)]);
        await CaptureAsync(data, "2027-01-01", [Line(data.Food.Id, 2m)]);
        await CaptureAsync(data, "2027-01-10", [Line(data.Food.Id, 3m)]);
        await CaptureAsync(data, "2027-01-11", [Line(data.Food.Id, 200m)]);

        var single = await ReportAsync(data.Home, "?from=2027-01-01&to=2027-01-01");
        var fortnight = await ReportAsync(data.Home, "?from=2026-12-28&to=2027-01-10");

        Assert.Equal(2m, Assert.Single(single.Categories).NetAmount);
        Assert.Equal(6m, Assert.Single(fortnight.Categories).NetAmount);
        Assert.Equal(new(3, 0, 0), fortnight.ReconciliationRecordCounts);
    }

    [Fact]
    public async Task RefundUsesItsOwnDateAndUnsignedReceiptDifferenceAfterOriginalPaymentIsVoided()
    {
        var data = await SeedAsync();
        var original = await CaptureAsync(data, "2026-12-01", [Line(data.Food.Id, 20m)], receipt: 20m);
        var refund = await CaptureAsync(data, "2027-01-05", [Line(data.Food.Id, 5m)],
            kind: "Refund", original: original.Id, receipt: 8m);
        await VoidAsync(data, original);
        var voidedRefund = await CaptureAsync(data, "2027-01-05", [Line(data.Food.Id, 10m)], kind: "Refund", receipt: 20m);
        await VoidAsync(data, voidedRefund);

        var january = await ReportAsync(data.Home, "?from=2027-01-05&to=2027-01-05");
        var december = await ReportAsync(data.Home);

        Assert.Equal(-5m, Assert.Single(january.Categories).NetAmount);
        Assert.Equal(new SpendingReceiptWarningResponse(refund.Id, new(2027, 1, 5), "Shop", "GBP", 8m, 5m, 3m),
            Assert.Single(january.Warnings));
        Assert.Equal(new(0, 0, 1), january.ReconciliationRecordCounts);
        Assert.Empty(december.Categories);
        Assert.Empty(december.Warnings);
        Assert.Equal(new(0, 0, 0), december.ReconciliationRecordCounts);
    }

    [Fact]
    public async Task SummaryReflectsCorrectionsAndPreservesSavedRecordStateAcrossReads()
    {
        var data = await SeedAsync();
        var record = await CaptureAsync(data, "2026-12-10", [Line(data.Food.Id, 10m)], receipt: 10m);
        using var correction = await _client.PutAsJsonAsync(data.Root + $"/records/{record.Id}/lines/{record.Lines[0].Id}", new
        {
            expectedRevision = record.Revision, netAmount = 7m, quantity = (decimal?)null,
            unitPrice = (decimal?)null, promotionStatus = "Unknown", notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        var before = await _client.GetStringAsync(data.Root + $"/records/{record.Id}");

        var report = await ReportAsync(data.Home);

        Assert.Equal(7m, Assert.Single(report.Categories).NetAmount);
        Assert.Equal(3m, Assert.Single(report.Warnings).Difference);
        Assert.Equal(new(0, 0, 1), report.ReconciliationRecordCounts);
        Assert.Equal(before, await _client.GetStringAsync(data.Root + $"/records/{record.Id}"));
    }

    [Fact]
    public async Task ResponseKeepsSavedCategoryAndMerchantLabelsAfterSetupRenames()
    {
        var data = await SeedAsync();
        await CaptureAsync(data, "2026-12-10", [Line(data.Food.Id, 3m)], receipt: 4m);
        using var categoryRename = await _client.PutAsJsonAsync(data.Root + $"/categories/{data.Food.Id}/name", new { name = "Groceries" });
        using var merchantRename = await _client.PutAsJsonAsync(data.Root + $"/merchants/{data.Merchant.Id}/name", new { name = "Renamed shop" });
        Assert.Equal(HttpStatusCode.OK, categoryRename.StatusCode);
        Assert.Equal(HttpStatusCode.OK, merchantRename.StatusCode);

        var report = await ReportAsync(data.Home);

        Assert.Equal("Food", Assert.Single(report.Categories).CategoryName);
        Assert.Equal("Shop", Assert.Single(report.Warnings).MerchantName);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task SummaryRouteHasNoMutationMethods(string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), Path(Guid.NewGuid()));
        await ProblemAsync(await _client.SendAsync(request), 405);
    }
}
