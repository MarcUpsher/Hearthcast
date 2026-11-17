using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record FindLastPaidPriceRequest : IValidatableObject
{
    [Required] public Guid? MerchantId { get; init; }
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")] public string? CurrencyCode { get; init; }
    [Required] public DateOnly? OnOrBefore { get; init; }
    public Guid? ExcludeRecordId { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((MerchantId, nameof(MerchantId)), (ExcludeRecordId, nameof(ExcludeRecordId)))
            .Concat(SpendingRecordRequestValidation.Date(OnOrBefore, nameof(OnOrBefore)));
}
