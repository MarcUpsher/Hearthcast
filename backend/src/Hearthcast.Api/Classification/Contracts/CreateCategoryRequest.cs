using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Classification;

namespace Hearthcast.Api.Classification.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCategoryRequest
{
    [Required]
    public string? Name { get; init; }

    [Required]
    [EnumDataType(typeof(FinancialType))]
    [JsonConverter(typeof(FinancialTypeJsonConverter))]
    public FinancialType? FinancialType { get; init; }
}
