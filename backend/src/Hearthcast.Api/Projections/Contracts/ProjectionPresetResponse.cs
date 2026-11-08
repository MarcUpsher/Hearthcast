using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

public sealed record ProjectionGroupResponse(Guid GroupId, string Bucket);
public sealed record ProjectionFinancedAssetResponse(Guid FinancedAssetId, decimal MonthlyLoanPaymentInAssetCurrency,
    decimal AnnualLoanInterestPercent, decimal AnnualAssetAppreciationPercent, decimal ExtraMonthlyLoanPaymentInAssetCurrency, string InvestmentCurrencyAfterPayoff);
public sealed record ProjectionInputsResponse(ProjectionAssumptionsResponse Assumptions,
    IReadOnlyList<ProjectionGroupResponse> Groups, IReadOnlyList<ProjectionFinancedAssetResponse> FinancedAssets);
public sealed record ProjectionPresetResponse(Guid Id, Guid HouseholdId, string Name, bool IsDefault, ProjectionInputsResponse Inputs,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc)
{
    internal static ProjectionPresetResponse From(ProjectionPreset p) => new(p.Id, p.HouseholdId, p.Name, p.IsDefault,
        new(ProjectionAssumptionsResponse.From(new(p.Name, p.CurrencyCode, p.DurationYears, p.AnnualInflationPercent, p.ShowInflationAdjustedValues,
                p.Currencies.OrderBy(c => c.CurrencyCode).Select(c => new ProjectionCurrencySettings(c.CurrencyCode, c.MonthlyInvestmentContribution,
                    c.AnnualInvestmentReturnPercent, c.MonthlySavingsContribution, c.AnnualSavingsInterestPercent)).ToArray())),
            p.Groups.OrderBy(g => g.GroupId).Select(g => new ProjectionGroupResponse(g.GroupId, g.Bucket.ToString())).ToArray(),
            p.FinancedAssets.OrderBy(a => a.FinancedAssetId).Select(a => new ProjectionFinancedAssetResponse(a.FinancedAssetId,
                a.MonthlyLoanPaymentInAssetCurrency, a.AnnualLoanInterestPercent, a.AnnualAssetAppreciationPercent,
                a.ExtraMonthlyLoanPaymentInAssetCurrency, a.InvestmentCurrencyAfterPayoff)).ToArray()),
        p.CreatedAtUtc, p.UpdatedAtUtc);
}
