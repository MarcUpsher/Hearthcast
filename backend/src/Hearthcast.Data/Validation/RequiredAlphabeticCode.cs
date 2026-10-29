using System.Runtime.CompilerServices;

namespace Hearthcast.Data.Validation;

internal static class RequiredAlphabeticCode
{
    public static string Normalize(
        string? value,
        int length,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        var code = RequiredText.Trim(value, parameterName).ToUpperInvariant();
        if (code.Length != length || code.Any(character => character < 'A' || character > 'Z'))
        {
            throw new ArgumentException($"Use a {length}-letter code.", parameterName);
        }

        return code;
    }
}
