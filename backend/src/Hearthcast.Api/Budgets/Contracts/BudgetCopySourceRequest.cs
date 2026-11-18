using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetCopySourceRequest : IValidatableObject
{
    [Required] public DateOnly? EffectiveFrom { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSnapshotRequestValidation.Date(EffectiveFrom, nameof(EffectiveFrom));
}
