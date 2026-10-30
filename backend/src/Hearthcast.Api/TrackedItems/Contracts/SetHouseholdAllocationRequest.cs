using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.TrackedItems.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetHouseholdAllocationRequest : IValidatableObject
{
    [Required]
    public bool? IncludeInHousehold { get; init; }

    public IReadOnlyList<AllocationShareRequest?>? Allocations { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        AllocationRequestValidation.Validate(IncludeInHousehold, Allocations);
}
