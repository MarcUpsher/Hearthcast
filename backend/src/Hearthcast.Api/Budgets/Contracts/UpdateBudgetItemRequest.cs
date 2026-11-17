using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateBudgetItemRequest : IValidatableObject
{
    [Required] public Guid? ExpenseCategoryId { get; init; }
    [Required] public string? Name { get; init; }
    [JsonRequired] public Guid? MerchantId { get; init; }
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")] public string? CurrencyCode { get; init; }
    [Required, JsonConverter(typeof(BudgetFrequencyJsonConverter))] public BudgetFrequency? Frequency { get; init; }
    [Required, Range(1, int.MaxValue), JsonNumberHandling(JsonNumberHandling.Strict)] public int? IntervalCount { get; init; }
    [JsonRequired] public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSetupRequestValidation.Ids((ExpenseCategoryId, nameof(ExpenseCategoryId)), (MerchantId, nameof(MerchantId)))
            .Concat(BudgetSetupRequestValidation.Text((Name, nameof(Name)), (CurrencyCode, nameof(CurrencyCode)), (Notes, nameof(Notes))));
}
