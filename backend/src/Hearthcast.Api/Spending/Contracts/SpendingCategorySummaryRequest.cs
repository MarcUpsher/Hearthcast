using System.ComponentModel.DataAnnotations;

namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingCategorySummaryRequest : IValidatableObject
{
    [Required] public DateOnly? From { get; init; }
    [Required] public DateOnly? To { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in SpendingRecordRequestValidation.Date(From, nameof(From))
            .Concat(SpendingRecordRequestValidation.Date(To, nameof(To))))
            yield return error;
        if (From > To)
            yield return new ValidationResult("Start date must not follow end date.", [nameof(From), nameof(To)]);
    }
}
