using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Classification.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RenameGroupRequest
{
    [Required]
    public string? Name { get; init; }
}
