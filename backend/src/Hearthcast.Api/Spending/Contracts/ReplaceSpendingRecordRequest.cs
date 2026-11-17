using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceSpendingRecordRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    [Required] public Guid? MerchantId { get; init; }
    [Required] public DateOnly? SpendingDate { get; init; }
    [Required] public IReadOnlyList<SpendingLineRequest?>? Lines { get; init; }
    [JsonRequired] public decimal? ReceiptTotal { get; init; }
    [JsonRequired] public string? ExternalReference { get; init; }
    [JsonRequired] public string? Notes { get; init; }
    public IReadOnlyList<RememberSpendingPriceRequest?>? RememberPrices { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)), (MerchantId, nameof(MerchantId)))
            .Concat(SpendingRecordRequestValidation.Date(SpendingDate, nameof(SpendingDate)))
            .Concat(SpendingRecordRequestValidation.Collections(Lines, RememberPrices))
            .Concat(SpendingRequestValidation.Text((ExternalReference, nameof(ExternalReference)), (Notes, nameof(Notes))));
}
