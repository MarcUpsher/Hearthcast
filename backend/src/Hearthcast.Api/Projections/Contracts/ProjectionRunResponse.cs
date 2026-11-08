using Hearthcast.Data.Projections.Calculation;
using Hearthcast.Data.Projections.Preparation;

namespace Hearthcast.Api.Projections.Contracts;

public sealed record ProjectionIssueResponse(string Kind, string Severity, Guid? SourceId, string Message);
public sealed record ProjectionWarningResponse(Guid FinancedAssetId, string Kind, DateOnly FirstOccurrence);
public sealed record ProjectionCoverageResponse(Guid SnapshotEntryId, Guid SourceId, string Kind, Guid GroupId,
    string GroupName, string CategoryName, string Name, string CurrencyCode, string FinancialType,
    string Disposition, decimal? Value, decimal? LoanBalance, bool UsesCarriedValues);
public sealed record ProjectionRunResponse(Guid SnapshotId, Guid SnapshotRevision, string CurrencyCode,
    bool CanCalculate, IReadOnlyList<ProjectionMonth>? Months, IReadOnlyList<ProjectionIssueResponse> Issues,
    IReadOnlyList<ProjectionCoverageResponse> Coverage, IReadOnlyList<ProjectionWarningResponse> Warnings,
    ProjectionGrowthSummary? GrowthBreakdown, ProjectionExpenseTargetSummary? ExpenseTarget)
{
    internal static ProjectionRunResponse From(ProjectionPreparation p, ProjectionResult? result) =>
        new(p.SnapshotId, p.SnapshotRevision, p.Assumptions.CurrencyCode, p.CanCalculate, result?.Months,
            p.Issues.Select(i => new ProjectionIssueResponse(i.Kind.ToString(), i.Severity.ToString(), i.SourceId, i.Message)).ToArray(),
            p.Coverage.Select(c => new ProjectionCoverageResponse(c.SnapshotEntryId, c.SourceId, c.Kind.ToString(), c.GroupId,
                c.GroupName, c.CategoryName, c.Name, c.CurrencyCode, c.FinancialType.ToString(), c.Disposition.ToString(),
                c.Value, c.LoanBalance, c.UsesCarriedValues)).ToArray(),
            result?.Warnings.Select(w => new ProjectionWarningResponse(w.FinancedAssetId, w.Kind.ToString(), w.FirstOccurrence)).ToArray() ?? [],
            result?.GrowthBreakdown, result?.ExpenseTarget);
}
