using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Spending.Contracts;

internal static class SpendingRecordRequestValidation
{
    internal static IEnumerable<ValidationResult> Collections(IReadOnlyList<SpendingLineRequest?>? lines,
        IReadOnlyList<RememberSpendingPriceRequest?>? prices)
    {
        if (lines is not null && (lines.Count == 0 || lines.Any(l => l is null)))
            yield return new ValidationResult("Supply at least one nonnull line.", ["Lines"]);
        foreach (var error in Prices(prices)) yield return error;
    }

    internal static IEnumerable<ValidationResult> Prices(IReadOnlyList<RememberSpendingPriceRequest?>? prices)
    {
        if (prices is not null && prices.Any(p => p is null))
            yield return new ValidationResult("Price choices cannot be null.", ["RememberPrices"]);
    }

    internal static IEnumerable<ValidationResult> Date(DateOnly? date, string name)
    {
        if (date == default(DateOnly)) yield return new ValidationResult("Supply a spending date.", [name]);
    }
}
