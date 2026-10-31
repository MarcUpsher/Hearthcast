using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.FinancedAssets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetFinancedAssetRetirementRequest
{
    [Required]
    public bool? IsRetired { get; init; }
}
