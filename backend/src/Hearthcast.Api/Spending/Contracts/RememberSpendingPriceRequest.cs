using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Spending;

namespace Hearthcast.Api.Spending.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RememberSpendingPriceRequest : IValidatableObject
{
    [Required, Range(1, int.MaxValue)] public int? LinePosition { get; init; }
    [Required] public decimal? ChosenUnitPrice { get; init; }
    public Guid? ExpectedRevision { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => SpendingRequestValidation.Ids((ExpectedRevision, nameof(ExpectedRevision)));
    internal RememberSpendingPrice ToValues() => new(LinePosition!.Value, ChosenUnitPrice!.Value, ExpectedRevision);
}
