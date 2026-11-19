using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Budgets;

internal static class BudgetCreateRequest
{
    internal static (BudgetSnapshotValues Values, string Hash) Freeze(Guid householdId, Guid snapshotId, BudgetSnapshotValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (snapshotId == Guid.Empty) throw new InvalidValueException("Supply a client-generated plan identifier.");
        BudgetRules.Date(values.EffectiveFrom);
        if (values.CopiedFromBudgetSnapshotId == Guid.Empty) throw new InvalidValueException("Supply a nonempty template identifier.");
        ArgumentNullException.ThrowIfNull(values.Entries);
        var entries = values.Entries.Select(Entry).OrderBy(e => e.BudgetItemId).ToArray();
        if (values.CopiedFromBudgetSnapshotId == snapshotId || entries.Any(e => e.Source?.SnapshotId == snapshotId))
            throw new InvalidValueException("A plan cannot use itself as a source.");
        if (entries.Select(e => e.BudgetItemId).Distinct().Count() != entries.Length)
            throw new InvalidValueException("Include a budget item only once per plan.");
        var copy = values with { Entries = entries, Notes = SpendingSetupRules.Notes(values.Notes) };
        // Persisted version-one format: request data only, independent of mutable setup/source state.
        var canonical = new
        {
            Version = 1, HouseholdId = householdId, SnapshotId = snapshotId,
            Date = Date(copy.EffectiveFrom), copy.CopiedFromBudgetSnapshotId, copy.Notes,
            Entries = entries.Select(e => new
            {
                e.BudgetItemId, Mode = (int)e.Mode, Amount = Number(e.PlannedAmount), ConfirmedOn = Date(e.ConfirmedOn),
                SourceSnapshotId = e.Source?.SnapshotId, SourceEntryId = e.Source?.EntryId, e.RefreshFromSetup,
                Metadata = e.Metadata is null ? null : new
                {
                    e.Metadata.ItemCode, e.Metadata.ItemName, e.Metadata.ExpenseCategoryId, e.Metadata.ExpenseCategoryName,
                    e.Metadata.CurrencyCode, Frequency = (int)e.Metadata.Frequency, e.Metadata.IntervalCount,
                    e.Metadata.MerchantId, e.Metadata.MerchantName, e.Metadata.Notes
                }
            }).ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        // Leave legacy canonical bytes untouched, including explicit default-kind metadata.
        if (entries.Any(e => e.Metadata?.Kind == BudgetItemKind.Allowance))
        {
            var versionTwo = JsonNode.Parse(bytes)!;
            versionTwo["Version"] = 2;
            for (var i = 0; i < entries.Length; i++)
                if (entries[i].Metadata is { } metadata) versionTwo["Entries"]![i]!["Metadata"]!["Kind"] = (int)metadata.Kind;
            bytes = JsonSerializer.SerializeToUtf8Bytes(versionTwo);
        }
        return (copy, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal static BudgetEntryValues Entry(BudgetEntryValues value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.BudgetItemId == Guid.Empty || !Enum.IsDefined(value.Mode))
            throw new InvalidValueException("Supply a budget item and a defined value mode.");
        if (value.Source is not null && (value.Source.SnapshotId == Guid.Empty || value.Source.EntryId == Guid.Empty))
            throw new InvalidValueException("Supply nonempty source plan and entry identifiers.");
        if (value.Mode == BudgetEntryValueMode.Confirm)
        {
            if (!value.PlannedAmount.HasValue || !value.ConfirmedOn.HasValue)
                throw new InvalidValueException("Explicit confirmation requires an amount and confirmation date.");
            BudgetRules.Amount(value.PlannedAmount);
            BudgetRules.Date(value.ConfirmedOn.Value);
        }
        else if (value.PlannedAmount.HasValue || value.ConfirmedOn.HasValue)
            throw new InvalidValueException("Only explicit confirmation accepts an entered amount/date.");
        if (value.RefreshFromSetup && value.Metadata is not null)
            throw new InvalidValueException("Select current setup or historical metadata, not both.");
        return value with { Metadata = value.Metadata is null ? null : Metadata(value.Metadata) };
    }

    internal static BudgetEntryMetadataValues Metadata(BudgetEntryMetadataValues value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ExpenseCategoryId == Guid.Empty || value.MerchantId == Guid.Empty
            || value.MerchantId.HasValue != (value.MerchantName is not null))
            throw new InvalidValueException("Supply a category and paired optional merchant identity/name.");
        if (!Enum.IsDefined(value.Kind) || (value.Kind == BudgetItemKind.Allowance && (value.MerchantId.HasValue || value.MerchantName is not null)))
            throw new InvalidValueException("Supply a defined budget kind; allowances do not have merchants.");
        BudgetRules.Recurrence(value.Frequency, value.IntervalCount);
        return value with
        {
            ItemCode = SpendingSetupRules.Code(value.ItemCode), ItemName = SpendingSetupRules.Text(value.ItemName),
            ExpenseCategoryName = SpendingSetupRules.Text(value.ExpenseCategoryName),
            CurrencyCode = BudgetRules.CurrencyCode(value.CurrencyCode),
            MerchantName = value.MerchantName is null ? null : SpendingSetupRules.Text(value.MerchantName),
            Notes = SpendingSetupRules.Notes(value.Notes)
        };
    }

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? Number(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture);
}
