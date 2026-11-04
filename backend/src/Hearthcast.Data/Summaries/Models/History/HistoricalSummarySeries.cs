namespace Hearthcast.Data.Summaries.Models.History;

public sealed record HistoricalSummarySeries(DateOnly From, DateOnly To, int IntervalMonths, string CurrencyCode,
    IReadOnlyList<string> AvailableCurrencies, IReadOnlyList<HistoricalSummaryPoint> Points);
