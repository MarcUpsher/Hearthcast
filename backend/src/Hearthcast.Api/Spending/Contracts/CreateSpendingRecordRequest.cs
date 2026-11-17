using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSpendingRecordRequest : IValidatableObject
{
    [Required] public Guid? Id { get; init; }
    [Required] public Guid? MerchantId { get; init; }
    [Required] public DateOnly? SpendingDate { get; init; }
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")] public string? CurrencyCode { get; init; }
    [Required, JsonConverter(typeof(SpendingRecordKindJsonConverter))] public SpendingRecordKind? Kind { get; init; }
    public Guid? OriginalPaymentId { get; init; }
    [Required] public IReadOnlyList<SpendingLineRequest?>? Lines { get; init; }
    public decimal? ReceiptTotal { get; init; }
    public string? ExternalReference { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<RememberSpendingPriceRequest?>? RememberPrices { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in SpendingRequestValidation.Ids((Id, nameof(Id)), (MerchantId, nameof(MerchantId)), (OriginalPaymentId, nameof(OriginalPaymentId)))) yield return error;
        foreach (var error in SpendingRecordRequestValidation.Date(SpendingDate, nameof(SpendingDate))) yield return error;
        foreach (var error in SpendingRecordRequestValidation.Collections(Lines, RememberPrices)) yield return error;
        foreach (var error in SpendingRequestValidation.Text((ExternalReference, nameof(ExternalReference)), (Notes, nameof(Notes)))) yield return error;
        if (OriginalPaymentId.HasValue && (Kind != SpendingRecordKind.Refund || OriginalPaymentId == Id))
            yield return new ValidationResult("Only a refund can reference a different original payment.", [nameof(OriginalPaymentId)]);
    }

    internal SpendingRecordValues ToValues() => new(MerchantId!.Value, SpendingDate!.Value, CurrencyCode!,
        Lines!.Select(l => l!.ToValues()).ToArray(), Kind!.Value, OriginalPaymentId, ReceiptTotal, ExternalReference, Notes);
}
