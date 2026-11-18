using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectBudgetAmountRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    [JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] public decimal? PlannedAmount { get; init; }
    [JsonRequired] public DateOnly? ConfirmedOn { get; init; }
    [JsonRequired] public string? Notes { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSetupRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)))
            .Concat(BudgetSnapshotRequestValidation.Amount(PlannedAmount, nameof(PlannedAmount)))
            .Concat(BudgetSnapshotRequestValidation.Date(ConfirmedOn, nameof(ConfirmedOn)))
            .Concat(BudgetSnapshotRequestValidation.Confirmation(PlannedAmount, ConfirmedOn))
            .Concat(BudgetSetupRequestValidation.Text((Notes, nameof(Notes))));
}
