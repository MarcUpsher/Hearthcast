using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionInputsRequest : IValidatableObject
{
    [Required] public ProjectionAssumptionsRequest? Assumptions { get; init; }
    [Required] public IReadOnlyList<ProjectionGroupRequest?>? Groups { get; init; }
    [Required] public IReadOnlyList<ProjectionFinancedAssetRequest?>? FinancedAssets { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Groups is { } groups && (groups.Any(g => g is null)
            || groups.Select(g => g?.GroupId).Distinct().Count() != groups.Count))
            yield return new("Supply each group once, in one bucket, without null entries.", [nameof(Groups)]);
        if (FinancedAssets is { } assets && (assets.Any(a => a is null)
            || assets.Select(a => a?.FinancedAssetId).Distinct().Count() != assets.Count))
            yield return new("Supply each financed asset once, without null entries.", [nameof(FinancedAssets)]);
    }

    internal ProjectionGroupSelection[] ToGroups() => Groups!.Select(g => new ProjectionGroupSelection(g!.GroupId!.Value, g.Bucket!.Value)).ToArray();
    internal ProjectionFinancedAssetSettings[] ToAssets() => FinancedAssets!.Select(a => new ProjectionFinancedAssetSettings(
        a!.FinancedAssetId!.Value, a.MonthlyLoanPaymentInAssetCurrency!.Value,
        a.AnnualLoanInterestPercent!.Value, a.AnnualAssetAppreciationPercent!.Value)).ToArray();
}
