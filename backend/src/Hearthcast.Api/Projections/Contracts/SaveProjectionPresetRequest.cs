using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveProjectionPresetRequest
{
    [Required] public string? Name { get; init; }
    [Required] public ProjectionInputsRequest? Inputs { get; init; }
}
