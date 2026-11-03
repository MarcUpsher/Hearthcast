using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Snapshots.Contracts;

internal static class SnapshotValueRequestValidation
{
    public static IEnumerable<ValidationResult> Validate(IReadOnlyList<ItemValueRequest?>? Values, IReadOnlyList<FinancedAssetValueRequest?>? FinancedAssetValues,
        string itemMemberName = "Values")
    {
        if (Values is { } values && (values.Any(row => row is null || row.TrackedItemId == Guid.Empty)
            || values.Select(row => row?.TrackedItemId).Distinct().Count() != values.Count))
            yield return new ValidationResult("Supply each item once, with a nonempty ID.", [itemMemberName]);
        if (FinancedAssetValues is { } assets && (assets.Any(row => row is null || row.FinancedAssetId == Guid.Empty)
            || assets.Select(row => (row?.FinancedAssetId, row?.Balance)).Distinct().Count() != assets.Count))
            yield return new ValidationResult("Supply each asset balance once, with a nonempty ID.", [nameof(FinancedAssetValues)]);
    }
}
