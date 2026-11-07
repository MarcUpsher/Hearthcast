using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionCurrencyRequest
{
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")]
    public string? CurrencyCode { get; init; }
    [Required] public decimal? MonthlyInvestmentContribution { get; init; }
    [Required] public decimal? AnnualInvestmentReturnPercent { get; init; }
    [Required] public decimal? MonthlySavingsContribution { get; init; }
    [Required] public decimal? AnnualSavingsInterestPercent { get; init; }

    internal ProjectionCurrencySettings ToSettings() => new(CurrencyCode!, MonthlyInvestmentContribution!.Value,
        AnnualInvestmentReturnPercent!.Value, MonthlySavingsContribution!.Value, AnnualSavingsInterestPercent!.Value);
}
