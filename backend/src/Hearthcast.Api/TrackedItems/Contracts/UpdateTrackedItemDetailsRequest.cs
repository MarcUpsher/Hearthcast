using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.TrackedItems.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateTrackedItemDetailsRequest : IValidatableObject
{
    [Required]
    public string? Name { get; init; }

    [JsonRequired]
    public string? InstitutionName { get; init; }

    [JsonRequired]
    public string? FundName { get; init; }

    [Required]
    public bool? IncludeInHousehold { get; init; }

    public IReadOnlyList<AllocationShareRequest?>? Allocations { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        AllocationRequestValidation.Validate(IncludeInHousehold, Allocations);
}
