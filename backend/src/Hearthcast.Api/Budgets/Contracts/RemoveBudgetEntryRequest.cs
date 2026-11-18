using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Budgets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RemoveBudgetEntryRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BudgetSetupRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)));
}
