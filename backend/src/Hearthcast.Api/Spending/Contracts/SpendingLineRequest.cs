using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpendingLineRequest : IValidatableObject
{
    [Required] public Guid? ExpenseCategoryId { get; init; }
    [Required] public decimal? NetAmount { get; init; }
    public Guid? SpendingItemId { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public string? UnitKey { get; init; }
    public string? UnitLabel { get; init; }
    public string? Description { get; init; }
    public string? ReceiptDescription { get; init; }
    public string? MerchantItemReference { get; init; }
    [JsonConverter(typeof(PromotionStatusJsonConverter))]
    public PromotionStatus PromotionStatus { get; init; } = PromotionStatus.Unknown;
    public string? Notes { get; init; }
    public bool IsAdditionalCost { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in SpendingRequestValidation.Ids((ExpenseCategoryId, nameof(ExpenseCategoryId)), (SpendingItemId, nameof(SpendingItemId)))) yield return error;
        if (Quantity.HasValue != UnitPrice.HasValue) yield return new ValidationResult("Supply quantity and unit price together.", [nameof(Quantity), nameof(UnitPrice)]);
        foreach (var error in SpendingRequestValidation.Text((UnitKey, nameof(UnitKey)), (UnitLabel, nameof(UnitLabel)),
            (Description, nameof(Description)), (ReceiptDescription, nameof(ReceiptDescription)),
            (MerchantItemReference, nameof(MerchantItemReference)), (Notes, nameof(Notes)))) yield return error;
    }

    internal SpendingLineValues ToValues() => new(ExpenseCategoryId!.Value, NetAmount!.Value, SpendingItemId,
        Quantity, UnitPrice, UnitKey, UnitLabel, Description, ReceiptDescription, MerchantItemReference, PromotionStatus, Notes, IsAdditionalCost);
}
