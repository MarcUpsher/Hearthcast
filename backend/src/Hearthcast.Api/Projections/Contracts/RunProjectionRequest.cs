using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RunProjectionRequest : IValidatableObject
{
    public Guid? PresetId { get; init; }
    public ProjectionInputsRequest? Inputs { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (PresetId.HasValue == (Inputs is not null))
            yield return new("Supply either a preset ID or unsaved inputs, exclusively.", [nameof(PresetId), nameof(Inputs)]);
        if (PresetId == Guid.Empty) yield return new("Select a preset.", [nameof(PresetId)]);
    }
}
