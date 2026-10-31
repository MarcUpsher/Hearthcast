using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Snapshots.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectSnapshotValuesRequest : IValidatableObject
{
    [Required]
    public Guid? ExpectedRevision { get; init; }
    public IReadOnlyList<ItemValueRequest?>? Values { get; init; }
    public IReadOnlyList<FinancedAssetValueRequest?>? FinancedAssetValues { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ExpectedRevision == Guid.Empty)
            yield return new ValidationResult("Supply the saved revision.", [nameof(ExpectedRevision)]);
        if ((Values?.Count ?? 0) + (FinancedAssetValues?.Count ?? 0) == 0)
            yield return new ValidationResult("Supply at least one value.", [nameof(Values)]);
        foreach (var error in SnapshotValueRequestValidation.Validate(Values, FinancedAssetValues))
            yield return error;
    }
}
