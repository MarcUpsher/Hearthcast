using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateUsualPriceRequest : IValidatableObject
{
    [Required]
    public Guid? SpendingItemId { get; init; }
    [Required]
    public Guid? MerchantId { get; init; }
    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{3}\s*$", ErrorMessage = "Use a three-letter currency code.")]
    public string? CurrencyCode { get; init; }
    [Required]
    public decimal? UsualUnitPrice { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((SpendingItemId, nameof(SpendingItemId)), (MerchantId, nameof(MerchantId)));
}
