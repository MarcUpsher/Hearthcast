using System.Text.Json;
using System.Text.Json.Serialization;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Spending;

public sealed record SpendingHistoryPage(IReadOnlyList<SpendingRecord> Records, string? NextCursor, int PageSize);

internal sealed record SpendingHistoryCursor(int Version, Guid HouseholdId, DateOnly? From, DateOnly? To,
    bool IncludeVoided, int PageSize, DateOnly Date, DateTimeOffset CreatedAtUtc, Guid Id)
{
    internal string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static SpendingHistoryCursor Decode(string cursor, Guid household, DateOnly? from, DateOnly? to,
        bool includeVoided, int pageSize)
    {
        try
        {
            if (cursor.Length is 0 or > 4096 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
                throw new FormatException();
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            var value = JsonSerializer.Deserialize<SpendingHistoryCursor>(Convert.FromBase64String(padded),
                new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            if (value is null || value.Version != 1 || value.HouseholdId != household || value.From != from || value.To != to
                || value.IncludeVoided != includeVoided || value.PageSize != pageSize || value.Id == Guid.Empty || value.Date == default
                || value.CreatedAtUtc == default || value.CreatedAtUtc.Offset != TimeSpan.Zero || value.CreatedAtUtc.Ticks % 10 != 0)
                throw new FormatException();
            return value;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new InvalidValueException("Invalid history cursor or changed history filters.", nameof(cursor));
        }
    }
}
