using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VoidSpendingRecordRequest : IValidatableObject
{
    [Required] public Guid? ExpectedRevision { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => SpendingRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)));
}
