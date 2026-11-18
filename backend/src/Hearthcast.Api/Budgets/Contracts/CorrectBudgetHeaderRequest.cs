using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorrectBudgetHeaderRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    [Required] public DateOnly? EffectiveFrom { get; init; }
    [JsonRequired] public string? Notes { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSetupRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)))
            .Concat(BudgetSnapshotRequestValidation.Date(EffectiveFrom, nameof(EffectiveFrom)))
            .Concat(BudgetSetupRequestValidation.Text((Notes, nameof(Notes))));
}
