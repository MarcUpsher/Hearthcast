namespace Hearthcast.Data.Projections.Calculation;

public enum ProjectionWarningKind { PaymentDoesNotReduceLoanBalance = 1 }

/// <summary>One warning per asset/kind, retaining the first occurrence.</summary>
public sealed record ProjectionWarning(Guid FinancedAssetId, ProjectionWarningKind Kind, DateOnly FirstOccurrence);
