using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

public sealed class ProjectionBucketJsonConverter() : JsonStringEnumConverter<ProjectionBucket>(allowIntegerValues: false);
