using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Snapshots.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RetryExchangeRatesRequest : IValidatableObject
{
    [Required]
    public Guid? ExpectedRevision { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ExpectedRevision == Guid.Empty)
            yield return new ValidationResult("Supply the saved revision.", [nameof(ExpectedRevision)]);
    }
}
