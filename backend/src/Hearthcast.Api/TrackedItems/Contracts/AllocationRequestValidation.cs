using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.TrackedItems.Contracts;

internal static class AllocationRequestValidation
{
    public static IEnumerable<ValidationResult> Validate(bool? includeInHousehold,
        IReadOnlyList<AllocationShareRequest?>? allocations)
    {
        if (allocations is null || includeInHousehold is null) yield break;

        if (allocations.Any(share => share is null || !share.PersonId.HasValue || share.PersonId.Value == Guid.Empty
            || !share.ShareBasisPoints.HasValue || share.ShareBasisPoints.Value is < 1 or > 10_000))
        {
            yield return new ValidationResult("Each allocation needs a person and a share from 1 to 10000 basis points.",
                ["Allocations"]);
            yield break;
        }

        var shares = allocations.Select(share => share!).ToArray();
        if (shares.Select(share => share.PersonId).Distinct().Count() != shares.Length)
            yield return new ValidationResult("Each person can have only one allocation.", ["Allocations"]);
        if (includeInHousehold.Value
            ? shares.Sum(share => (long)share.ShareBasisPoints!.Value) != 10_000
            : shares.Length != 0)
            yield return new ValidationResult("Included items need 100% in shares; excluded items need none.",
                ["Allocations"]);
    }
}
