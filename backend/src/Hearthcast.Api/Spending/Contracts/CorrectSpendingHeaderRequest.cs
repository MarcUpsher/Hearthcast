using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectSpendingHeaderRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    [Required] public DateOnly? SpendingDate { get; init; }
    [JsonRequired] public decimal? ReceiptTotal { get; init; }
    [JsonRequired] public string? ExternalReference { get; init; }
    [JsonRequired] public string? Notes { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)))
            .Concat(SpendingRecordRequestValidation.Date(SpendingDate, nameof(SpendingDate)))
            .Concat(SpendingRequestValidation.Text((ExternalReference, nameof(ExternalReference)), (Notes, nameof(Notes))));
}
