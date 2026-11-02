using System.Text.Json.Serialization;

namespace Hearthcast.Api.TrackedItems.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SetTrackedItemMetadataRequest
{
    [JsonRequired]
    public string? InstitutionName { get; init; }
    [JsonRequired]
    public string? FundName { get; init; }
}
