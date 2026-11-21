using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateSpendingItemRequest : IValidatableObject
{
    // Omission/null preserves the code for older clients.
    public string? Code { get; init; }
    [Required]
    public Guid? ExpenseCategoryId { get; init; }
    [Required]
    public string? Name { get; init; }
    [Required]
    public string? UnitLabel { get; init; }
    [JsonRequired]
    public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((ExpenseCategoryId, nameof(ExpenseCategoryId)))
            .Concat(SpendingRequestValidation.Text((Code, nameof(Code)), (Name, nameof(Name)), (UnitLabel, nameof(UnitLabel)),
                (Notes, nameof(Notes))));
}
