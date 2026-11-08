using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionAssumptionsRequest : IValidatableObject
{
    [Required]
    [RegularExpression(@"^\s*[A-Za-z]{3}\s*$")]
    public string? CurrencyCode { get; init; }
    [Required]
    public int? DurationYears { get; init; }
    [Required]
    public decimal? AnnualInflationPercent { get; init; }
    [Required]
    public bool? ShowInflationAdjustedValues { get; init; }

    [Required] public IReadOnlyList<ProjectionCurrencyRequest?>? Currencies { get; init; }
    public ProjectionExpenseTargetRequest? ExpenseTarget { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Currencies is { } rows && (rows.Count == 0 || rows.Any(c => c is null)
            || rows.Select(c => c?.CurrencyCode?.Trim().ToUpperInvariant()).Distinct().Count() != rows.Count))
            yield return new("Supply each currency pot once, without null entries.", [nameof(Currencies)]);
    }

    internal ProjectionPresetSettings ToSettings(string name = "") =>
        new(name, CurrencyCode!, DurationYears!.Value, AnnualInflationPercent!.Value,
            ShowInflationAdjustedValues!.Value, Currencies!.Select(c => c!.ToSettings()).ToArray(), ExpenseTarget?.ToSettings());
}
