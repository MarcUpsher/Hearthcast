using Hearthcast.Data.Classification;
using Hearthcast.Data.Summaries.Models;

namespace Hearthcast.Data.Summaries;

internal sealed record SnapshotReportGroup(string CurrencyCode, Guid CategoryId, string CategoryName,
    FinancialType FinancialType, Guid GroupId, string GroupName,
    IReadOnlyList<ItemSnapshotSummary> Items, IReadOnlyList<FinancedAssetSnapshotSummary> FinancedAssets);
