using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSpendingItemRequest : IValidatableObject
{
    [Required]
    public Guid? ExpenseCategoryId { get; init; }
    [Required]
    public string? Code { get; init; }
    [Required]
    public string? Name { get; init; }
    [Required]
    public string? UnitKey { get; init; }
    [Required]
    public string? UnitLabel { get; init; }
    public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((ExpenseCategoryId, nameof(ExpenseCategoryId)))
            .Concat(SpendingRequestValidation.Text((Code, nameof(Code)), (Name, nameof(Name)),
                (UnitKey, nameof(UnitKey)), (UnitLabel, nameof(UnitLabel)), (Notes, nameof(Notes))));
}
