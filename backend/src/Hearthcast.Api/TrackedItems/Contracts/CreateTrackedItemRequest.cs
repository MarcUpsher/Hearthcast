using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.TrackedItems.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateTrackedItemRequest : IValidatableObject
{
    [Required]
    public Guid? GroupId { get; init; }

    [Required]
    public Guid? HolderPersonId { get; init; }

    [Required]
    public string? Name { get; init; }

    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{3}\s*$", ErrorMessage = "Use a three-letter currency code.")]
    public string? CurrencyCode { get; init; }

    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{2}\s*$", ErrorMessage = "Use a two-letter country code.")]
    public string? CountryCode { get; init; }

    [Required]
    public bool? IncludeInHousehold { get; init; }

    public IReadOnlyList<AllocationShareRequest?>? Allocations { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (GroupId == Guid.Empty)
            yield return new ValidationResult("Select a group.", [nameof(GroupId)]);
        if (HolderPersonId == Guid.Empty)
            yield return new ValidationResult("Select a holder.", [nameof(HolderPersonId)]);
        foreach (var error in AllocationRequestValidation.Validate(IncludeInHousehold, Allocations))
            yield return error;
    }
}
