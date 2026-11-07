using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionAssumptionsRequest
{
    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{3}\s*$")]
    public string? CurrencyCode { get; init; }
    [Required]
    public int? DurationYears { get; init; }
    [Required]
    public decimal? MonthlyInvestmentContribution { get; init; }
    [Required]
    public decimal? AnnualInvestmentReturnPercent { get; init; }
    [Required]
    public decimal? MonthlySavingsContribution { get; init; }
    [Required]
    public decimal? AnnualSavingsInterestPercent { get; init; }
    [Required]
    public decimal? AnnualInflationPercent { get; init; }
    [Required]
    public bool? ShowInflationAdjustedValues { get; init; }

    internal ProjectionPresetSettings ToSettings(string name = "") =>
        new(name, CurrencyCode!, DurationYears!.Value, MonthlyInvestmentContribution!.Value,
            AnnualInvestmentReturnPercent!.Value, MonthlySavingsContribution!.Value,
            AnnualSavingsInterestPercent!.Value, AnnualInflationPercent!.Value, ShowInflationAdjustedValues!.Value);
}
