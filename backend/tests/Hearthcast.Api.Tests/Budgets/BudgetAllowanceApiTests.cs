using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hearthcast.Api.Budgets.Contracts;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Tests.Budgets;

public sealed partial class BudgetSetupApiTests
{
    [Fact]
    public async Task AllowanceHttpWorkflowUsesGeneratedIdentityAndSavedKindAcrossReadEndpointsAndRetries()
    {
        var d = await SeedAsync();
        var item = await CreateAsync(d.Root, new { kind = "Allowance", expenseCategoryId = d.Category.Id, currencyCode = "GBP", frequency = "Month", intervalCount = 1 });
        Assert.Equal(BudgetItemKind.Commitment, d.Item.Kind); Assert.Equal(BudgetItemKind.Allowance, item.Kind);
        Assert.StartsWith("ALW_", item.Code); Assert.Equal(d.Category.Name, item.Name); Assert.Null(item.MerchantId); Assert.Null(item.PaymentAnchorDate);
        var root = $"/api/households/{d.Home}/budgets/snapshots";
        var request = new { id = Guid.NewGuid(), effectiveFrom = "2026-01-01", entries = new[] {
            new { budgetItemId = item.Id, mode = "Confirm", plannedAmount = 400.123456m, confirmedOn = "2026-01-01" },
            new { budgetItemId = d.Item.Id, mode = "Confirm", plannedAmount = 50m, confirmedOn = "2026-01-01" } } };
        using var response = await _client.PostAsJsonAsync(root, request); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<BudgetSnapshotResponse>())!;
        Assert.Equal(400.123456m, plan.Entries.Single(e => e.Kind == BudgetItemKind.Allowance).PlannedAmount);
        foreach (var path in new[] { root + "/" + plan.Id, root + "/copy-source?effectiveFrom=2026-02-01" })
            Assert.Equivalent(plan, await _client.GetFromJsonAsync<BudgetSnapshotResponse>(path));
        var history = (await _client.GetFromJsonAsync<BudgetHistoryPageResponse>(root))!;
        Assert.Contains(history.Records[0].Entries, e => e.Kind == BudgetItemKind.Allowance);
        var effective = (await _client.GetFromJsonAsync<BudgetEffectivePlanResponse>(root + "/effective?asOf=2026-01-15"))!;
        Assert.Contains(effective.Snapshot.Entries, e => e.Kind == BudgetItemKind.Allowance);
        var current = (await _client.GetFromJsonAsync<BudgetCurrentOutgoingsResponse>($"/api/households/{d.Home}/budgets/current-outgoings?asOf=2026-01-15"))!;
        Assert.Equal(BudgetItemKind.Allowance, current.Items.Single(r => r.Item.Id == item.Id).Entry!.Kind);
        using var retry = await _client.PostAsJsonAsync(root, request); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var duplicate = await _client.PostAsJsonAsync(d.Root, new { kind = "Allowance", expenseCategoryId = d.Category.Id, currencyCode = "GBP", frequency = "Week" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var update = new { expenseCategoryId = d.Category.Id, name = item.Name, merchantId = (Guid?)null, currencyCode = "GBP", frequency = "Year", intervalCount = 1, notes = "Annual target", paymentAnchorDate = (string?)null };
        using var edited = await _client.PutAsJsonAsync(d.Root + "/" + item.Id + "/details", update); Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(BudgetItemKind.Allowance, (await edited.Content.ReadFromJsonAsync<BudgetItemResponse>())!.Kind);
        foreach (var retired in new[] { true, false }) {
            using var changed = await _client.PutAsJsonAsync(d.Root + "/" + item.Id + "/retirement", new { isRetired = retired }); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }
    }

    [Theory]
    [InlineData("kind", null)]
    [InlineData("kind", "Other")]
    [InlineData("kind", "1")]
    [InlineData("kind", "Commitment, Allowance")]
    [InlineData("kind", 2)]
    [InlineData("code", "FOOD")]
    [InlineData("name", "Food")]
    [InlineData("paymentAnchorDate", "2026-01-01")]
    public async Task AllowanceCreateRejectsInvalidOrInapplicableFields(string key, object? value)
    {
        var d = await SeedAsync();
        var body = new Dictionary<string, object?> { ["kind"] = "Allowance", ["expenseCategoryId"] = d.Category.Id, ["currencyCode"] = "GBP", ["frequency"] = "Month", [key] = value };
        using var response = await _client.PostAsJsonAsync(d.Root, body); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AllowanceMetadataCannotBeSpoofedAndKindIsNotAnUpdateField()
    {
        var d = await SeedAsync();
        var item = await CreateAsync(d.Root, new { kind = "Allowance", expenseCategoryId = d.Category.Id, currencyCode = "GBP", frequency = "Month" });
        var root = $"/api/households/{d.Home}/budgets/snapshots";
        foreach (var kind in new object?[] { "Commitment", null, 2, "Other" })
        {
            var metadata = new { kind, itemCode = item.Code, itemName = item.Name, expenseCategoryId = d.Category.Id, expenseCategoryName = d.Category.Name,
                currencyCode = "GBP", frequency = "Month", intervalCount = 1, merchantId = (Guid?)null, merchantName = (string?)null, notes = (string?)null };
            using var response = await _client.PostAsJsonAsync(root, new { id = Guid.NewGuid(), effectiveFrom = "2026-01-01", entries = new[] { new { budgetItemId = item.Id, mode = "Unrecorded", metadata } } });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        var body = Body(d, true); body["kind"] = "Allowance";
        using var update = await _client.PutAsJsonAsync(d.ItemPath + "/details", body); Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        body.Remove("kind"); body["merchantId"] = d.Merchant.Id;
        using var merchant = await _client.PutAsJsonAsync(d.Root + "/" + item.Id + "/details", body); Assert.Equal(HttpStatusCode.BadRequest, merchant.StatusCode);
        var foreign = await SeedAsync();
        using var foreignCreate = await _client.PostAsJsonAsync(d.Root, new { kind = "Allowance", expenseCategoryId = foreign.Category.Id, currencyCode = "GBP", frequency = "Month" });
        Assert.Equal(HttpStatusCode.NotFound, foreignCreate.StatusCode);
        var wire = await _client.GetFromJsonAsync<JsonElement>(d.Root + "/" + item.Id); Assert.Equal("Allowance", wire.GetProperty("kind").GetString());
    }
}
