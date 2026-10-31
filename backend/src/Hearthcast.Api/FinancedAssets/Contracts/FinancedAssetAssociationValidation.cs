using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.FinancedAssets.Contracts;

internal static class FinancedAssetAssociationValidation
{
    public static IEnumerable<ValidationResult> Validate(bool? includeInHousehold,
        IReadOnlyList<FinancedAssetShareRequest?>? owners, IReadOnlyList<Guid>? loanHolderPersonIds,
        IReadOnlyList<FinancedAssetShareRequest?>? householdAllocations)
    {
        if (loanHolderPersonIds is { } loans
            && (loans.Contains(Guid.Empty) || loans.Distinct().Count() != loans.Count))
            yield return new ValidationResult("Loan holders must be distinct people with nonempty IDs.", [nameof(CreateFinancedAssetRequest.LoanHolderPersonIds)]);
        foreach (var error in ValidateShares(owners, true, nameof(CreateFinancedAssetRequest.Owners)))
            yield return error;
        if (includeInHousehold.HasValue)
            foreach (var error in ValidateShares(householdAllocations, includeInHousehold.Value,
                         nameof(CreateFinancedAssetRequest.HouseholdAllocations)))
                yield return error;
    }

    private static IEnumerable<ValidationResult> ValidateShares(IReadOnlyList<FinancedAssetShareRequest?>? shares,
        bool requireFullShare, string field)
    {
        if (shares is null) yield break;
        if (shares.Any(share => share is null || !share.PersonId.HasValue || share.PersonId == Guid.Empty
            || !share.ShareBasisPoints.HasValue || share.ShareBasisPoints.Value is < 1 or > 10_000))
        {
            yield return new ValidationResult("Each share needs a person and 1 to 10000 basis points.", [field]);
            yield break;
        }
        if (shares.Select(share => share!.PersonId).Distinct().Count() != shares.Count)
            yield return new ValidationResult("People must be unique within each association.", [field]);
        if (requireFullShare ? shares.Sum(share => (long)share!.ShareBasisPoints!.Value) != 10_000 : shares.Count != 0)
            yield return new ValidationResult("Ownership and included household shares must total 100%; excluded household shares must be empty.", [field]);
    }
}
