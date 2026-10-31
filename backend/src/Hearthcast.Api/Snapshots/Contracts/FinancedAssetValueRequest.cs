using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Snapshots;

namespace Hearthcast.Api.Snapshots.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinancedAssetValueRequest
{
    [Required]
    public Guid? FinancedAssetId { get; init; }
    [Required, EnumDataType(typeof(FinancedAssetBalanceKind))]
    public FinancedAssetBalanceKind? Balance { get; init; }
    [Range(typeof(decimal), "0", "79228162514264337593543950335")]
    public decimal? Amount { get; init; }
}
