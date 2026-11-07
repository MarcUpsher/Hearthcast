using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionGroupRequest : IValidatableObject
{
    [Required] public Guid? GroupId { get; init; }
    [Required, EnumDataType(typeof(ProjectionBucket))]
    [JsonConverter(typeof(ProjectionBucketJsonConverter))]
    public ProjectionBucket? Bucket { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (GroupId == Guid.Empty) yield return new("Select a group.", [nameof(GroupId)]);
    }
}
