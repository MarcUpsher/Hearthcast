using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Spending.Contracts;

namespace Hearthcast.Api.Tests.Spending;

public sealed partial class SpendingRecordApiTests
{
    [Fact]
    public async Task AdditionalCostCaptureReadHistorySummaryCorrectionAndReplacementRetainClassification()
    {
        var d = await SeedAsync(); var body = Body(d, true); var item = Line(d, true); item["netAmount"] = 45.98m; item["quantity"] = 2m; item["unitPrice"] = 22.99m;
        var cost = new Dictionary<string, object?> { ["expenseCategoryId"] = d.Category.Id, ["netAmount"] = 3.99m, ["description"] = "Delivery", ["isAdditionalCost"] = true };
        body["lines"] = new[] { item, cost }; body["receiptTotal"] = 49.97m;
        var record = await CreateAsync(d, body);
        Assert.Equal(45.98m, record.PurchaseAmount); Assert.Equal(3.99m, record.AdditionalCostAmount); Assert.Equal(49.97m, record.TotalAmount);
        Assert.True(record.Lines[1].IsAdditionalCost); Assert.False(record.Lines[0].IsAdditionalCost); Assert.Equal("Reconciled", record.ReconciliationStatus);
        using var replay = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var read = (await _client.GetFromJsonAsync<SpendingRecordResponse>(d.Records + "/" + record.Id))!; Assert.True(read.Lines[1].IsAdditionalCost);
        Assert.True((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!.Records.Single().Lines[1].IsAdditionalCost);
        var summary = await _client.GetFromJsonAsync<JsonElement>(d.Root + "/summary?from=2026-11-17&to=2026-11-17");
        Assert.Equal(49.97m, summary.GetProperty("categories")[0].GetProperty("netAmount").GetDecimal()); Assert.Equal(0, summary.GetProperty("warnings").GetArrayLength());
        using var corrected = await _client.PutAsJsonAsync(d.Records + $"/{record.Id}/lines/{record.Lines[1].Id}", new {
            expectedRevision = record.Revision, netAmount = 4.99m, quantity = (decimal?)null, unitPrice = (decimal?)null, promotionStatus = "Unknown", notes = "Courier" });
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode); var changed = (await corrected.Content.ReadFromJsonAsync<SpendingRecordResponse>())!;
        Assert.True(changed.Lines[1].IsAdditionalCost); Assert.Equal(record.Lines[0].Id, changed.Lines[0].Id); Assert.Equal(50.97m, changed.TotalAmount);
        cost["netAmount"] = 4.99m; using var replacement = await _client.PutAsJsonAsync(d.Records + "/" + record.Id, new {
            expectedRevision = changed.Revision, merchantId = d.Merchant.Id, spendingDate = record.SpendingDate, lines = new[] { item, cost },
            receiptTotal = 50.97m, externalReference = (string?)null, notes = "Replaced" });
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode); Assert.True((await replacement.Content.ReadFromJsonAsync<SpendingRecordResponse>())!.Lines[1].IsAdditionalCost);
        cost["isAdditionalCost"] = false;
        using var conflict = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Theory]
    [InlineData("netAmount", 0)] [InlineData("netAmount", -1)] [InlineData("netAmount", 0.001)]
    [InlineData("description", " ")] [InlineData("isAdditionalCost", "true")] [InlineData("isAdditionalCost", null)]
    public async Task AdditionalCostRejectsInvalidAmountsDescriptionAndFlagShape(string key, object? value)
    {
        var d = await SeedAsync(); var body = Body(d);
        var cost = new Dictionary<string, object?> { ["expenseCategoryId"] = d.Category.Id, ["netAmount"] = 3.99m, ["description"] = "Delivery", ["isAdditionalCost"] = true, [key] = value };
        body["lines"] = new[] { Line(d), cost };
        using var response = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<SpendingHistoryPageResponse>(d.Records))!.Records);
    }

    [Fact]
    public async Task AdditionalCostCannotBeACatalogueItemAndLegacyOmissionDefaultsToNormal()
    {
        var d = await SeedAsync(); var body = Body(d, true); var line = Line(d, true); line["isAdditionalCost"] = true; line["description"] = "Delivery"; body["lines"] = new[] { line };
        using var invalid = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        line.Remove("isAdditionalCost"); var record = await CreateAsync(d, body);
        Assert.False(record.Lines[0].IsAdditionalCost); Assert.Equal(record.TotalAmount, record.PurchaseAmount); Assert.Equal(0, record.AdditionalCostAmount);
        line["isAdditionalCost"] = false;
        using var replay = await _client.PostAsJsonAsync(d.Records, body); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
    }
}
