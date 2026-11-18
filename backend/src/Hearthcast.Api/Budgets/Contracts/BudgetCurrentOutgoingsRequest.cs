using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetCurrentOutgoingsRequest : IValidatableObject
{
    public DateOnly? AsOf { get; init; }
    public bool IncludeRetired { get; init; } = true;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSnapshotRequestValidation.Date(AsOf, nameof(AsOf));
}
