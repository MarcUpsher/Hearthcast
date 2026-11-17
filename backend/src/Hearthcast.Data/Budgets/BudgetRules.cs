using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Budgets;

internal static class BudgetRules
{
    // Preserving a saved currency does not select it anew from today's capture policy.
    internal static string Currency(string value, string? savedCurrency = null)
    {
        var code = CurrencyCode(value);
        return code == savedCurrency ? code : SpendingCurrencyPolicy.Normalize(code);
    }

    internal static string CurrencyCode(string value)
    {
        var code = SpendingSetupRules.Text(value).ToUpperInvariant();
        if (code.Length != 3 || code.Any(c => c is < 'A' or > 'Z'))
            throw new InvalidValueException("Use a three-letter currency code.");
        return code;
    }

    internal static void Recurrence(BudgetFrequency frequency, int intervalCount)
    {
        if (!Enum.IsDefined(frequency) || intervalCount <= 0)
            throw new InvalidValueException("Use Week, Month or Year and a positive recurrence interval.");
    }

    internal static decimal? Amount(decimal? value)
    {
        if (value.HasValue && (value < 0 || value >= 1_000_000_000_000m || decimal.Round(value.Value, 6) != value))
            throw new InvalidValueException("Use a nonnegative planned amount below one trillion with at most six decimals.");
        return value;
    }

    internal static DateOnly Date(DateOnly date)
    {
        // Npgsql reserves DateOnly.MinValue/MaxValue for PostgreSQL infinities.
        if (date == DateOnly.MinValue || date == DateOnly.MaxValue)
            throw new InvalidValueException("Supply a finite budget date between 0001-01-02 and 9999-12-30.");
        return date;
    }

    internal static void Identity(Guid id, string creationRequestHash)
    {
        if (id == Guid.Empty || creationRequestHash is null || creationRequestHash.Length != 64
            || creationRequestHash.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f')))
            throw new InvalidValueException("Supply a nonempty client identifier and canonical SHA-256 create-request hash.");
    }
}
