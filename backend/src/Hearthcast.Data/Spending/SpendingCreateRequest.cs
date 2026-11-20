using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

internal static class SpendingCreateRequest
{
    internal static (SpendingRecordValues Values, RememberSpendingPrice[] Prices, string Hash) Freeze(
        Guid householdId, Guid recordId, SpendingRecordValues values, IReadOnlyList<RememberSpendingPrice>? prices)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (recordId == Guid.Empty) throw new InvalidValueException("Supply a client-generated record identifier.");
        ArgumentNullException.ThrowIfNull(values.Lines);
        if (values.Lines.Count == 0 || values.Lines.Any(l => l is null)) throw new InvalidValueException("Supply at least one line.");
        // Freeze caller-owned collections and canonicalise only request data, never mutable setup names.
        var lines = values.Lines.Select(l => l with
        {
            UnitKey = l.UnitKey is null ? null : SpendingSetupRules.UnitKey(l.UnitKey),
            UnitLabel = l.UnitLabel is null ? null : SpendingSetupRules.Text(l.UnitLabel),
            Description = SpendingSetupRules.Notes(l.Description), ReceiptDescription = SpendingSetupRules.Notes(l.ReceiptDescription),
            MerchantItemReference = SpendingSetupRules.Notes(l.MerchantItemReference), Notes = SpendingSetupRules.Notes(l.Notes)
        }).ToArray();
        var discounts = (values.Discounts ?? []).Select(d => d is null
            ? throw new InvalidValueException("Discounts cannot contain null entries.")
            : d with { Description = SpendingSetupRules.Notes(d.Description) }).ToArray();
        var copy = values with
        {
            CurrencyCode = RequiredAlphabeticCode.Normalize(values.CurrencyCode, 3), Lines = lines, Discounts = discounts,
            ExternalReference = SpendingSetupRules.Notes(values.ExternalReference), Notes = SpendingSetupRules.Notes(values.Notes)
        };
        var choices = (prices ?? []).ToArray();
        if (choices.Any(p => p is null)) throw new InvalidValueException("Price choices cannot contain null entries.");
        choices = choices.OrderBy(p => p.LinePosition).ToArray();
        // This version-one canonical format is a persisted idempotency contract.
        var canonical = new
        {
            Version = 1, HouseholdId = householdId, RecordId = recordId, copy.MerchantId,
            Date = copy.SpendingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), copy.CurrencyCode,
            Kind = (int)copy.Kind, copy.OriginalPaymentId, ReceiptTotal = Number(copy.ReceiptTotal),
            copy.ExternalReference, copy.Notes,
            Lines = lines.Select(l => new
            {
                l.ExpenseCategoryId, NetAmount = Number(l.NetAmount), l.SpendingItemId,
                Quantity = Number(l.Quantity), UnitPrice = Number(l.UnitPrice), l.UnitKey, l.UnitLabel,
                l.Description, l.ReceiptDescription, l.MerchantItemReference, Promotion = (int)l.PromotionStatus, l.Notes
            }).ToArray(),
            Prices = choices.Select(p => new { p.LinePosition, Amount = Number(p.ChosenUnitPrice), p.ExpectedRevision }).ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        if (discounts.Length > 0)
        {
            var versionTwo = JsonNode.Parse(bytes)!; versionTwo["Version"] = 2;
            versionTwo["Discounts"] = JsonSerializer.SerializeToNode(discounts.Select(d => new { d.ExpenseCategoryId, Amount = Number(d.Amount), d.Description }).ToArray());
            bytes = JsonSerializer.SerializeToUtf8Bytes(versionTwo);
        }
        var costPositions = lines.Select((line, index) => (line, position: index + 1))
            .Where(x => x.line.IsAdditionalCost).Select(x => x.position).ToArray();
        if (costPositions.Length > 0)
        {
            // Versions one and two stay byte-for-byte compatible when every flag is false.
            var versionThree = JsonNode.Parse(bytes)!; versionThree["Version"] = 3;
            versionThree["AdditionalCostLinePositions"] = JsonSerializer.SerializeToNode(costPositions);
            bytes = JsonSerializer.SerializeToUtf8Bytes(versionThree);
        }
        return (copy, choices, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static string? Number(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture);
}
