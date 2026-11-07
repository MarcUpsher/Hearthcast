using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectionFinancedAssetRequest : IValidatableObject
{
    [Required] public Guid? FinancedAssetId { get; init; }
    [Required] public decimal? MonthlyLoanPaymentInAssetCurrency { get; init; }
    [Required] public decimal? AnnualLoanInterestPercent { get; init; }
    [Required] public decimal? AnnualAssetAppreciationPercent { get; init; }
    [Required] public decimal? ExtraMonthlyLoanPaymentInAssetCurrency { get; init; }
    [Required, RegularExpression(@"^\s*[A-Za-z]{3}\s*$")]
    public string? InvestmentCurrencyAfterPayoff { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (FinancedAssetId == Guid.Empty) yield return new("Select a financed asset.", [nameof(FinancedAssetId)]);
    }
}
