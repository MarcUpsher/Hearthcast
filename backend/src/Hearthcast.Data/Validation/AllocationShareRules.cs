using System.Runtime.CompilerServices;

namespace Hearthcast.Data.Validation;

internal static class AllocationShareRules
{
    public const int FullShareBasisPoints = 10_000;

    public static void EnsureValid(
        int shareBasisPoints,
        [CallerArgumentExpression(nameof(shareBasisPoints))] string? parameterName = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shareBasisPoints, 1, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(shareBasisPoints, FullShareBasisPoints, parameterName);
    }
}
