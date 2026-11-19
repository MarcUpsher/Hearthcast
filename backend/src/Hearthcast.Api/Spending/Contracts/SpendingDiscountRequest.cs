using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SpendingDiscountRequest : IValidatableObject
{
    [Required] public Guid? ExpenseCategoryId { get; init; }
    [Required, JsonNumberHandling(JsonNumberHandling.Strict)] public decimal? Amount { get; init; }
    public string? Description { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        foreach (var error in SpendingRequestValidation.Ids((ExpenseCategoryId, nameof(ExpenseCategoryId)))
            .Concat(SpendingRequestValidation.Text((Description, nameof(Description))))) yield return error;
        if (Amount.HasValue && (Amount <= 0 || Amount >= 1_000_000_000_000m)) yield return new ValidationResult("Enter a positive discount below one trillion.", [nameof(Amount)]);
    }
    internal SpendingDiscountValues ToValues() => new(ExpenseCategoryId!.Value, Amount!.Value, Description);
    internal static IEnumerable<ValidationResult> Collection(IReadOnlyList<SpendingDiscountRequest?>? values)
    {
        if (values?.Any(value => value is null) == true) yield return new ValidationResult("Discounts cannot contain null entries.", ["Discounts"]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectSpendingDiscountsRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    [Required] public IReadOnlyList<SpendingDiscountRequest?>? Discounts { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context) => SpendingRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)))
        .Concat(SpendingDiscountRequest.Collection(Discounts));
}
