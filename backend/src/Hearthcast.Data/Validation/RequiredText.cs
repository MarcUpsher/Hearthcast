using System.Runtime.CompilerServices;

namespace Hearthcast.Data.Validation;

internal static class RequiredText
{
    public static string Trim(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidValueException("A nonblank value is required.", parameterName);
        }
        if (value.Contains('\0'))
        {
            throw new InvalidValueException("Text cannot contain null characters.", parameterName);
        }
        return value.Trim();
    }
}
