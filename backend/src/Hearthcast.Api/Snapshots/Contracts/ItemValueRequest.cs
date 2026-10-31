using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hearthcast.Api.Snapshots.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemValueRequest
{
    [Required]
    public Guid? TrackedItemId { get; init; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? Amount { get; init; }
}
