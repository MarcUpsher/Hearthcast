using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

internal static class SpendingSetupRules
{
    internal const int MaximumCodeLength = 64;
    internal const int MaximumUnitKeyLength = 32;
    // Explicit whitespace policy shared with PostgreSQL descriptive-text constraints.
    internal const string TrimCharacters = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
    private static readonly char[] TrimCharacterArray = TrimCharacters.ToCharArray();

    internal static string Text(string? value)
    {
        if (value is null || value.Contains('\0'))
            throw new InvalidValueException("Nonblank text without null characters is required.", nameof(value));
        var text = value.Trim(TrimCharacterArray);
        if (text.Length == 0)
            throw new InvalidValueException("A nonblank value is required.", nameof(value));
        return text;
    }

    internal static string Code(string value)
    {
        var code = Text(value).ToUpperInvariant();
        if (code.Length > MaximumCodeLength)
            throw new InvalidValueException($"Use at most {MaximumCodeLength} characters for an item code.", nameof(value));
        if (!IsKey(code.ToLowerInvariant()))
            throw new InvalidValueException("Use letters, digits, dots, underscores or hyphens, starting with a letter or digit.", nameof(value));
        return code;
    }

    internal static string UnitKey(string value)
    {
        var key = Text(value).ToLowerInvariant();
        if (key.Length > MaximumUnitKeyLength)
            throw new InvalidValueException($"Use at most {MaximumUnitKeyLength} characters for a unit key.", nameof(value));
        if (!IsKey(key))
            throw new InvalidValueException("Use letters, digits, dots, underscores or hyphens, starting with a letter or digit.", nameof(value));
        return key;
    }

    private static bool IsKey(string key) => key.Length > 0
        && (char.IsAsciiLetterLower(key[0]) || char.IsAsciiDigit(key[0]))
        && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-');

    internal static string? Notes(string? value) =>
        value is null || value.Trim(TrimCharacterArray).Length == 0 ? null : Text(value);

    internal static decimal UsualPrice(decimal value)
    {
        if (value < 0 || value >= 1_000_000_000m || decimal.Round(value, 6) != value)
            throw new InvalidValueException("Use a nonnegative price below one billion with at most six decimal places.", nameof(value));
        return value;
    }
}
