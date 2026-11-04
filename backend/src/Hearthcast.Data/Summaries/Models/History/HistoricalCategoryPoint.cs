using Hearthcast.Data.Classification;
using Hearthcast.Data.Summaries.Models.Merged;

namespace Hearthcast.Data.Summaries.Models.History;

public sealed record HistoricalCategoryPoint(Guid CategoryId, string CategoryName, FinancialType FinancialType,
    MergedAmount Amount);
