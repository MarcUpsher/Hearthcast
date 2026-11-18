using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetAsOfRequest : IValidatableObject
{
    public DateOnly? AsOf { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSnapshotRequestValidation.Date(AsOf, nameof(AsOf));
}
