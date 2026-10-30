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
        if (Allocations is null || IncludeInHousehold is null) yield break;

        if (Allocations.Any(share => share is null || !share.PersonId.HasValue || share.PersonId.Value == Guid.Empty
            || !share.ShareBasisPoints.HasValue || share.ShareBasisPoints.Value is < 1 or > 10_000))
        {
            yield return new ValidationResult("Each allocation needs a person and a share from 1 to 10000 basis points.",
                [nameof(Allocations)]);
            yield break;
        }

        var shares = Allocations.Select(share => share!).ToArray();
        if (shares.Select(share => share.PersonId).Distinct().Count() != shares.Length)
            yield return new ValidationResult("Each person can have only one allocation.", [nameof(Allocations)]);
        if (IncludeInHousehold.Value
            ? shares.Sum(share => (long)share.ShareBasisPoints!.Value) != 10_000
            : shares.Length != 0)
            yield return new ValidationResult("Included items need 100% in shares; excluded items need none.",
                [nameof(Allocations)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllocationShareRequest
{
    public Guid? PersonId { get; init; }
    public int? ShareBasisPoints { get; init; }
}
