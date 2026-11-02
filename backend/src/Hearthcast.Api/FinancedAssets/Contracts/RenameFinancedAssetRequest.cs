using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.FinancedAssets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RenameFinancedAssetRequest
{
    [Required]
    public string? Name { get; init; }
}
