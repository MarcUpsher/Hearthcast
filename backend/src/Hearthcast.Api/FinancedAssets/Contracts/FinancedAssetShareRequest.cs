using System.Text.Json.Serialization;

namespace Hearthcast.Api.FinancedAssets.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinancedAssetShareRequest
{
    public Guid? PersonId { get; init; }
    public int? ShareBasisPoints { get; init; }
}
