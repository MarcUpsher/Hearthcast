using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateUsualPriceRequest : IValidatableObject
{
    [Required]
    public Guid? ExpectedRevision { get; init; }
    [Required]
    public decimal? UsualUnitPrice { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        SpendingRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)));
}
