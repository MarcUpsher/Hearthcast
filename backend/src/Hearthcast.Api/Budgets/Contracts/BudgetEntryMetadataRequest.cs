using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BudgetEntryMetadataRequest : IValidatableObject
{
    [Required, JsonConverter(typeof(BudgetItemKindJsonConverter))] public BudgetItemKind? Kind { get; init; } = BudgetItemKind.Commitment;
    [Required] public string? ItemCode { get; init; }
    [Required] public string? ItemName { get; init; }
    [Required] public Guid? ExpenseCategoryId { get; init; }
    [Required] public string? ExpenseCategoryName { get; init; }
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")] public string? CurrencyCode { get; init; }
    [Required, JsonConverter(typeof(BudgetFrequencyJsonConverter))] public BudgetFrequency? Frequency { get; init; }
    [Required, Range(1, int.MaxValue), JsonNumberHandling(JsonNumberHandling.Strict)] public int? IntervalCount { get; init; }
    [JsonRequired] public Guid? MerchantId { get; init; }
    [JsonRequired] public string? MerchantName { get; init; }
    [JsonRequired] public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in BudgetSetupRequestValidation.Ids((ExpenseCategoryId, nameof(ExpenseCategoryId)), (MerchantId, nameof(MerchantId)))) yield return error;
        foreach (var error in BudgetSetupRequestValidation.Text((ItemCode, nameof(ItemCode)), (ItemName, nameof(ItemName)),
            (ExpenseCategoryName, nameof(ExpenseCategoryName)), (CurrencyCode, nameof(CurrencyCode)),
            (MerchantName, nameof(MerchantName)), (Notes, nameof(Notes)))) yield return error;
        if (Kind == BudgetItemKind.Allowance && (MerchantId.HasValue || MerchantName is not null))
            yield return new ValidationResult("Allowances do not have merchants.", [nameof(MerchantId), nameof(MerchantName)]);
        if (MerchantId.HasValue != (MerchantName is not null))
            yield return new ValidationResult("Merchant identity and name must both be supplied or both be null.", [nameof(MerchantId), nameof(MerchantName)]);
    }

    internal BudgetEntryMetadataValues ToValues() => new(ItemCode!, ItemName!, ExpenseCategoryId!.Value, ExpenseCategoryName!,
        CurrencyCode!, Frequency!.Value, IntervalCount!.Value, MerchantId, MerchantName, Notes, Kind!.Value);
}
