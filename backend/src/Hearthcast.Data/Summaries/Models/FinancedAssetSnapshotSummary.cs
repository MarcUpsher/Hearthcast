namespace Hearthcast.Data.Summaries.Models;

public sealed record FinancedAssetSnapshotSummary(Guid FinancedAssetSnapshotId, Guid FinancedAssetId,
    string AssetName, string CountryCode, DateOnly? PurchaseDate, decimal? PurchasePrice,
    decimal OriginalLoanAmount, bool HasWithdrawalFacility, bool IncludeInHousehold,
    IReadOnlyList<SnapshotPersonShare> Owners, IReadOnlyList<Guid> LoanHolderSnapshotPersonIds,
    IReadOnlyList<SnapshotPersonShare> HouseholdAllocations,
    SnapshotBalanceSummary CurrentValuation, SnapshotBalanceSummary OutstandingLoanBalance,
    SnapshotBalanceSummary AvailableToWithdraw, decimal? CurrentEquity, decimal? LoanBalanceReduction,
    decimal? LoanBalanceReductionExcludingWithdrawableFunds);
