using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectSpendingLineRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    [Required] public decimal? NetAmount { get; init; }
    [JsonRequired] public decimal? Quantity { get; init; }
    [JsonRequired] public decimal? UnitPrice { get; init; }
    [Required, JsonConverter(typeof(PromotionStatusJsonConverter))] public PromotionStatus? PromotionStatus { get; init; }
    [JsonRequired] public string? Notes { get; init; }
    public IReadOnlyList<RememberSpendingPriceRequest?>? RememberPrices { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in SpendingRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)))) yield return error;
        foreach (var error in SpendingRequestValidation.Text((Notes, nameof(Notes)))) yield return error;
        foreach (var error in SpendingRecordRequestValidation.Prices(RememberPrices)) yield return error;
        if (Quantity.HasValue != UnitPrice.HasValue) yield return new ValidationResult("Supply quantity and unit price together.", [nameof(Quantity), nameof(UnitPrice)]);
    }
}
