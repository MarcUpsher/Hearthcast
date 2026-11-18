using System.ComponentModel.DataAnnotations;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed record BudgetHistoryRequest : IValidatableObject
{
    public DateOnly? Before { get; init; }
    [Range(1, BudgetSnapshotService.MaximumHistoryPageSize)] public int PageSize { get; init; } = BudgetSnapshotService.DefaultHistoryPageSize;
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSnapshotRequestValidation.Date(Before, nameof(Before));
}
