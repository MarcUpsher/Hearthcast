using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateBudgetItemRequest : IValidatableObject
{
    [Required] public Guid? ExpenseCategoryId { get; init; }
    [Required, JsonConverter(typeof(BudgetItemKindJsonConverter))] public BudgetItemKind? Kind { get; init; } = BudgetItemKind.Commitment;
    public string? Code { get; init; }
    public string? Name { get; init; }
    public Guid? MerchantId { get; init; }
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")] public string? CurrencyCode { get; init; }
    [Required, JsonConverter(typeof(BudgetFrequencyJsonConverter))] public BudgetFrequency? Frequency { get; init; }
    [Required, Range(1, int.MaxValue), JsonNumberHandling(JsonNumberHandling.Strict)] public int? IntervalCount { get; init; } = 1;
    public string? Notes { get; init; }
    public DateOnly? PaymentAnchorDate { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in BudgetSetupRequestValidation.Ids((ExpenseCategoryId, nameof(ExpenseCategoryId)), (MerchantId, nameof(MerchantId)))
            .Concat(BudgetSetupRequestValidation.Text((Code, nameof(Code)), (Name, nameof(Name)),
                (CurrencyCode, nameof(CurrencyCode)), (Notes, nameof(Notes))))
            .Concat(BudgetSnapshotRequestValidation.Date(PaymentAnchorDate, nameof(PaymentAnchorDate)))) yield return error;
        if (Kind == BudgetItemKind.Commitment && (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name)))
            yield return new ValidationResult("Commitments require code and name.", [nameof(Code), nameof(Name)]);
        if (Kind == BudgetItemKind.Allowance && (Code is not null || Name is not null || MerchantId.HasValue || PaymentAnchorDate.HasValue))
            yield return new ValidationResult("Allowances generate their identity and do not accept code, name, merchant or payment date.");
    }
}
