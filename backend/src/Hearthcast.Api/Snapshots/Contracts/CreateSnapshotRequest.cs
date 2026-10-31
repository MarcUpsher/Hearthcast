using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Snapshots;

namespace Hearthcast.Api.Snapshots.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSnapshotRequest : IValidatableObject
{
    [Required]
    public DateOnly? SnapshotDate { get; init; }
    [EnumDataType(typeof(DatePrecision))]
    public DatePrecision DatePrecision { get; init; } = DatePrecision.Day;
    public IReadOnlyList<ItemValueRequest?>? Values { get; init; }
    public IReadOnlyList<FinancedAssetValueRequest?>? FinancedAssetValues { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Values is { } values && (values.Any(row => row is null || row.TrackedItemId == Guid.Empty)
            || values.Select(row => row?.TrackedItemId).Distinct().Count() != values.Count))
            yield return new ValidationResult("Supply each item once, with a nonempty ID.", [nameof(Values)]);
        if (FinancedAssetValues is { } assets && (assets.Any(row => row is null || row.FinancedAssetId == Guid.Empty)
            || assets.Select(row => (row?.FinancedAssetId, row?.Balance)).Distinct().Count() != assets.Count))
            yield return new ValidationResult("Supply each asset balance once, with a nonempty ID.", [nameof(FinancedAssetValues)]);
    }
}
