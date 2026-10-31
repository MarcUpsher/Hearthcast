using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Snapshots;

namespace Hearthcast.Api.Snapshots.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateNextSnapshotRequest : IValidatableObject
{
    [Required]
    public DateOnly? SnapshotDate { get; init; }
    [EnumDataType(typeof(DatePrecision))]
    public DatePrecision DatePrecision { get; init; } = DatePrecision.Day;
    public IReadOnlyList<ItemValueRequest?>? Values { get; init; }
    public IReadOnlyList<FinancedAssetValueRequest?>? FinancedAssetValues { get; init; }

    public Guid? SourceSnapshotId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SnapshotValueRequestValidation.Validate(Values, FinancedAssetValues);
}
