using Hearthcast.Data.Projections;

namespace Hearthcast.Api.Projections.Contracts;

public sealed record ProjectionGroupResponse(Guid GroupId, string Bucket);
public sealed record ProjectionFinancedAssetResponse(Guid FinancedAssetId, decimal MonthlyLoanPaymentInAssetCurrency,
    decimal AnnualLoanInterestPercent, decimal AnnualAssetAppreciationPercent);
public sealed record ProjectionInputsResponse(ProjectionAssumptionsResponse Assumptions,
    IReadOnlyList<ProjectionGroupResponse> Groups, IReadOnlyList<ProjectionFinancedAssetResponse> FinancedAssets);
public sealed record ProjectionPresetResponse(Guid Id, Guid HouseholdId, string Name, ProjectionInputsResponse Inputs,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc)
{
    internal static ProjectionPresetResponse From(ProjectionPreset p) => new(p.Id, p.HouseholdId, p.Name,
        new(ProjectionAssumptionsResponse.From(new(p.Name, p.CurrencyCode, p.DurationYears,
                p.MonthlyInvestmentContribution, p.AnnualInvestmentReturnPercent, p.MonthlySavingsContribution,
                p.AnnualSavingsInterestPercent, p.AnnualInflationPercent, p.ShowInflationAdjustedValues)),
            p.Groups.OrderBy(g => g.GroupId).Select(g => new ProjectionGroupResponse(g.GroupId, g.Bucket.ToString())).ToArray(),
            p.FinancedAssets.OrderBy(a => a.FinancedAssetId).Select(a => new ProjectionFinancedAssetResponse(a.FinancedAssetId,
                a.MonthlyLoanPaymentInAssetCurrency, a.AnnualLoanInterestPercent, a.AnnualAssetAppreciationPercent)).ToArray()),
        p.CreatedAtUtc, p.UpdatedAtUtc);
}
