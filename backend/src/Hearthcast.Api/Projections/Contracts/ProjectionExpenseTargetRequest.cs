using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionExpenseTargetRequest
{
    [Required] public decimal? AnnualExpenses { get; init; }
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")] public string? CurrencyCode { get; init; }
    [Required] public decimal? AnnualWithdrawalRatePercent { get; init; }
    [Required] public bool? IncludeSavings { get; init; }

    internal ProjectionExpenseTargetSettings ToSettings() => new(AnnualExpenses!.Value, CurrencyCode!,
        AnnualWithdrawalRatePercent!.Value, IncludeSavings!.Value);
}
