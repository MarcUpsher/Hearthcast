namespace Hearthcast.Data.Summaries.Models.History;

public sealed record HistoricalPersonPoint(Guid PersonId, string DisplayName,
    IReadOnlyList<HistoricalCategoryPoint> Categories);
