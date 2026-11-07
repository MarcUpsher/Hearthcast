using Hearthcast.Data.Classification;

namespace Hearthcast.Data.Projections.Preparation;

public enum ProjectionCoverageKind { Item = 1, FinancedAsset = 2 }
public enum ProjectionCoverageDisposition
{
    Investment = 1, Savings = 2, FinancedAsset = 3,
    NotSelected = 4, ExcludedFromHousehold = 5, InvalidSelection = 6
}

/// <summary>All copied snapshot entries; source amounts are native, and debt item values are unsigned.</summary>
public sealed record ProjectionCoverageEntry(Guid SnapshotEntryId, Guid SourceId, ProjectionCoverageKind Kind,
    Guid GroupId, string GroupName, string CategoryName, string Name, string CurrencyCode,
    FinancialType FinancialType, ProjectionCoverageDisposition Disposition, decimal? Value, decimal? LoanBalance, bool UsesCarriedValues);
