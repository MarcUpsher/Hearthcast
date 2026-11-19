using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Spending.Contracts;

namespace Hearthcast.Api.Tests.Spending;

public sealed partial class SpendingRecordApiTests
{
    [Fact]
    public async Task ReceiptDiscountCaptureCorrectionHistorySummaryAndReplayUseNetSpending()
    {
        var d = await SeedAsync(); var body = Body(d); body["receiptTotal"] = 7.90m;
        body["discounts"] = new[] { new { expenseCategoryId = d.Category.Id, amount = 2.10m, description = "£8 Stir Fry Dine In" } };
        var record = await CreateAsync(d, body);
        Assert.Equal(10, record.GrossAmount); Assert.Equal(2.10m, record.DiscountAmount); Assert.Equal(7.90m, record.TotalAmount);
        Assert.Equal("Reconciled", record.ReconciliationStatus); Assert.Equal("£8 Stir Fry Dine In", record.Discounts[0].Description);
        using var replay = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var history = (await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!; Assert.Equal(2.10m, history.Records[0].DiscountAmount);
        var summary = await _client.GetFromJsonAsync<JsonElement>(d.Root + "/summary?from=2026-11-17&to=2026-11-17");
        Assert.Equal(7.90m, summary.GetProperty("categories")[0].GetProperty("netAmount").GetDecimal()); Assert.Equal(0, summary.GetProperty("warnings").GetArrayLength());
        using var correct = await _client.PutAsJsonAsync(d.Records + "/" + record.Id + "/discounts", new { expectedRevision = record.Revision,
            discounts = new[] { new { expenseCategoryId = d.Category.Id, amount = 3m, description = "Updated deal" } } });
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode); var changed = (await correct.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        Assert.Equal(7, changed.CountedAmount); Assert.Equal(record.Lines[0].Id, changed.Lines[0].Id); Assert.Equal(record.Lines[0].ExpenseCategoryName, changed.Lines[0].ExpenseCategoryName);
        using var stale = await _client.PutAsJsonAsync(d.Records + "/" + record.Id + "/discounts", new { expectedRevision = record.Revision, discounts = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var cleared = await _client.PutAsJsonAsync(d.Records + "/" + record.Id + "/discounts", new { expectedRevision = changed.Revision, discounts = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode); Assert.Equal(10m, (await cleared.Content.ReadFromJsonAsync<SpendingRecordResponse>())!.TotalAmount);
    }
    [Fact]
    public async Task ReceiptDiscountLegacyReplacementKeepsDiscountsAndExplicitEmptyArrayClearsThem()
    {
        var d = await SeedAsync(); var body = Body(d); body["discounts"] = new[] { new { expenseCategoryId = d.Category.Id, amount = 1m } };
        var record = await CreateAsync(d, body); var replacement = new Dictionary<string, object?> {
            ["expectedRevision"] = record.Revision, ["merchantId"] = d.Merchant.Id, ["spendingDate"] = record.SpendingDate,
            ["lines"] = new[] { Line(d) }, ["receiptTotal"] = null, ["externalReference"] = null, ["notes"] = null };
        using var saved = await _client.PutAsJsonAsync(d.Records + "/" + record.Id, replacement); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        record = (await saved.Content.ReadFromJsonAsync<SpendingRecordResponse>())!; Assert.Equal(1m, record.DiscountAmount);
        replacement["expectedRevision"] = record.Revision; replacement["discounts"] = null;
        using var invalid = await _client.PutAsJsonAsync(d.Records + "/" + record.Id, replacement); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        replacement["discounts"] = Array.Empty<object>(); using var cleared = await _client.PutAsJsonAsync(d.Records + "/" + record.Id, replacement);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode); Assert.Empty((await cleared.Content.ReadFromJsonAsync<SpendingRecordResponse>())!.Discounts);
    }
    [Theory]
    [InlineData("amount", -1)] [InlineData("amount", 0)] [InlineData("amount", "2.10")] [InlineData("amount", null)]
    [InlineData("expenseCategoryId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("unexpected", "field")]
    public async Task ReceiptDiscountDiscountNestedValidationRejectsInvalidValues(string key, object? value)
    {
        var d = await SeedAsync(); var body = Body(d);
        var discount = new Dictionary<string, object?> { ["expenseCategoryId"] = d.Category.Id, ["amount"] = 2m, [key] = value };
        body["discounts"] = new[] { discount }; using var response = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task ReceiptDiscountNullEntriesForeignReferencesOverdrawAndVoidedEditsAreRejected()
    {
        var d = await SeedAsync(); var body = Body(d); body["discounts"] = new object?[] { null };
        using var invalid = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var foreign = await SeedAsync(); body["discounts"] = new[] { new { expenseCategoryId = foreign.Category.Id, amount = 1m } };
        using var missing = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        body["discounts"] = new[] { new { expenseCategoryId = d.Category.Id, amount = 11m } };
        using var overdraw = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.BadRequest, overdraw.StatusCode);
        body.Remove("discounts"); var record = await CreateAsync(d, body);
        using var voided = await _client.PostAsJsonAsync(d.Records + "/" + record.Id + "/void", new { expectedRevision = record.Revision });
        record = (await voided.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        using var edit = await _client.PutAsJsonAsync(d.Records + "/" + record.Id + "/discounts", new { expectedRevision = record.Revision, discounts = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }
}
