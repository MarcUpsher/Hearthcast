using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Budgets.Contracts;

internal static class BudgetSnapshotRequestValidation
{
    internal static IEnumerable<ValidationResult> Date(DateOnly? value, string name)
    {
        if (value.HasValue && (value == DateOnly.MinValue || value == DateOnly.MaxValue))
            yield return new ValidationResult("Supply a finite budget date between 0001-01-02 and 9999-12-30.", [name]);
    }

    internal static IEnumerable<ValidationResult> Amount(decimal? value, string name)
    {
        if (value.HasValue && (value < 0 || value >= 1_000_000_000_000m || decimal.Round(value.Value, 6) != value))
            yield return new ValidationResult("Use a nonnegative amount below one trillion with at most six decimals.", [name]);
    }

    internal static IEnumerable<ValidationResult> Confirmation(decimal? amount, DateOnly? confirmedOn)
    {
        if (amount.HasValue != confirmedOn.HasValue)
            yield return new ValidationResult("An entered amount and its confirmation date must be supplied together.", ["PlannedAmount", "ConfirmedOn"]);
    }
}
