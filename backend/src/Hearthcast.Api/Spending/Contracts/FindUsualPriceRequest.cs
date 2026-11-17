using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record FindUsualPriceRequest : IValidatableObject
{
    [Required]
    public Guid? MerchantId { get; init; }
    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{3}\s*$", ErrorMessage = "Use a three-letter currency code.")]
    public string? CurrencyCode { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((MerchantId, nameof(MerchantId)));
}
