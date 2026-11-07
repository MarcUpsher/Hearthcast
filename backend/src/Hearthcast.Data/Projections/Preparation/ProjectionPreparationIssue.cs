namespace Hearthcast.Data.Projections.Preparation;

public enum ProjectionIssueSeverity { Warning = 1, Error = 2 }
public enum ProjectionPreparationIssueKind
{
    MissingGroup = 1, NoHouseholdItems = 2, DebtGroupSelected = 3,
    MissingFinancedAsset = 4, ExcludedFinancedAsset = 5,
    MissingValue = 6, MissingExchangeRate = 7, CarriedValue = 8
}

public sealed record ProjectionPreparationIssue(ProjectionPreparationIssueKind Kind,
    ProjectionIssueSeverity Severity, Guid? SourceId, string Message);
