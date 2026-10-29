using System.Runtime.CompilerServices;

namespace Hearthcast.Data.Validation;

internal static class RequiredText
{
    public static string Trim(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
