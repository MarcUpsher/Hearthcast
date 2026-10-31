using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.FinancedAssets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetFinancedAssetAssociationsRequest : IValidatableObject
{
    [Required]
    public IReadOnlyList<FinancedAssetShareRequest?>? Owners { get; init; }
    [Required]
    public IReadOnlyList<Guid>? LoanHolderPersonIds { get; init; }
    [Required]
    public bool? IncludeInHousehold { get; init; }
    [Required]
    public IReadOnlyList<FinancedAssetShareRequest?>? HouseholdAllocations { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        FinancedAssetAssociationValidation.Validate(IncludeInHousehold, Owners, LoanHolderPersonIds, HouseholdAllocations);
}
