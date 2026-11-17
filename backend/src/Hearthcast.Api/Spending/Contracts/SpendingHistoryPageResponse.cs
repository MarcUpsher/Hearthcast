namespace Hearthcast.Api.Spending.Contracts;

public sealed record SpendingHistoryPageResponse(IReadOnlyList<SpendingRecordResponse> Records, string? NextCursor, int PageSize);
