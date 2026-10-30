using System.Text.Json.Serialization;

namespace Hearthcast.Api.TrackedItems.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllocationShareRequest
{
    public Guid? PersonId { get; init; }
    public int? ShareBasisPoints { get; init; }
}
