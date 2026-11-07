using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Projections;

internal static class ProjectionRules
{
    public static void Contribution(decimal value, string name)
    {
        if (value < 0) throw new InvalidValueException("Amounts cannot be negative.", name);
    }

    public static void Rate(decimal value, string name, bool allowNegative = true)
    {
        if (value > 100 || (allowNegative ? value <= -100 : value < 0))
            throw new InvalidValueException(allowNegative
                ? "Annual percentages must be greater than -100 and at most 100."
                : "Loan interest must be between 0 and 100 percent.", name);
    }
}
