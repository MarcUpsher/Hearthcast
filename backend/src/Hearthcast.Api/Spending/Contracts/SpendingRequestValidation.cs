using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Spending.Contracts;

internal static class SpendingRequestValidation
{
    internal static IEnumerable<ValidationResult> Ids(params (Guid? Value, string Name)[] fields) =>
        fields.Where(f => f.Value == Guid.Empty)
            .Select(f => new ValidationResult("Supply a nonempty identifier.", [f.Name]));

    internal static IEnumerable<ValidationResult> Text(params (string? Value, string Name)[] fields) =>
        fields.Where(f => f.Value?.Contains('\0') == true)
            .Select(f => new ValidationResult("Text cannot contain null characters.", [f.Name]));
}
