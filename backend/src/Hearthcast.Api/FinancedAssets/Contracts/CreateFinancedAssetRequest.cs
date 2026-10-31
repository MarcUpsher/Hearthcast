using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.FinancedAssets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFinancedAssetRequest : IValidatableObject
{
    [Required]
    public Guid? GroupId { get; init; }
    [Required]
    public string? Name { get; init; }
    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{3}\s*$")]
    public string? CurrencyCode { get; init; }
    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{2}\s*$")]
    public string? CountryCode { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchasePrice { get; init; }
    [Required]
    public decimal? OriginalLoanAmount { get; init; }
    [Required]
    public bool? HasWithdrawalFacility { get; init; }
    [Required]
    public bool? IncludeInHousehold { get; init; }
    [Required]
    public IReadOnlyList<FinancedAssetShareRequest?>? Owners { get; init; }
    [Required]
    public IReadOnlyList<Guid>? LoanHolderPersonIds { get; init; }
    [Required]
    public IReadOnlyList<FinancedAssetShareRequest?>? HouseholdAllocations { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (GroupId == Guid.Empty)
            yield return new ValidationResult("Select a group.", [nameof(GroupId)]);
        if (OriginalLoanAmount < 0)
            yield return new ValidationResult("Original loan amount cannot be negative.", [nameof(OriginalLoanAmount)]);
        if (PurchasePrice < 0)
            yield return new ValidationResult("Purchase price cannot be negative.", [nameof(PurchasePrice)]);
        if (LoanHolderPersonIds is { } loans
            && (loans.Contains(Guid.Empty) || loans.Distinct().Count() != loans.Count))
            yield return new ValidationResult("Loan holders must be distinct people with nonempty IDs.", [nameof(LoanHolderPersonIds)]);
        foreach (var error in ValidateShares(Owners, true, nameof(Owners))) yield return error;
        if (IncludeInHousehold.HasValue)
            foreach (var error in ValidateShares(HouseholdAllocations, IncludeInHousehold.Value, nameof(HouseholdAllocations)))
                yield return error;
    }

    private static IEnumerable<ValidationResult> ValidateShares(IReadOnlyList<FinancedAssetShareRequest?>? shares,
        bool requireFullShare, string field)
    {
        if (shares is null) yield break; // Required handles missing/null collections.
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
