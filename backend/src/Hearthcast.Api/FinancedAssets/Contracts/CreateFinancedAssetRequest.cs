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
        foreach (var error in FinancedAssetAssociationValidation.Validate(IncludeInHousehold,
                     Owners, LoanHolderPersonIds, HouseholdAllocations))
            yield return error;
    }
}
