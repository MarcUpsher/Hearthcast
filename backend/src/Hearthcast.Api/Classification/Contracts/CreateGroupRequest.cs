using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Classification.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateGroupRequest
{
    [Required]
    public string? Name { get; init; }
}
