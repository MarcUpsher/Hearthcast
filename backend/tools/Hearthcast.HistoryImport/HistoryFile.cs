using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Snapshots;

namespace Hearthcast.HistoryImport;

internal sealed record HistoryFile(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("importKind")] string ImportKind,
    [property: JsonPropertyName("tables")] HistoryTables Tables)
{
    internal static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    internal static async Task<T> ReadAsync<T>(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token)
            ?? throw new InvalidDataException("The input file is empty.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new ExactDecimalConverter());
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}

internal sealed record HistoryTables(
    HouseholdRow[] Households, PersonRow[] People, CategoryRow[] Categories, GroupRow[] Groups,
    ItemRow[] Items, [property: JsonPropertyName("Financed assets")] AssetRow[] FinancedAssets,
    AssociationRow[] Associations, SnapshotRow[] Snapshots,
    [property: JsonPropertyName("Item values")] ItemValueRow[] ItemValues,
    [property: JsonPropertyName("Asset values")] AssetValueRow[] AssetValues);

internal sealed record HouseholdRow(string HouseholdRef, string Name);
internal sealed record PersonRow(string PersonRef, string HouseholdRef, string DisplayName, bool ParticipatesInHouseholdFinances);
internal sealed record CategoryRow(string CategoryRef, string HouseholdRef, string Name, FinancialType FinancialType);
internal sealed record GroupRow(string GroupRef, string CategoryRef, string Name);
internal sealed record ItemRow(string ItemRef, string GroupRef, string HolderPersonRef, string Name,
    string CurrencyCode, string CountryCode, bool IncludeInHousehold, bool IsRetired);
internal sealed record AssetRow(string AssetRef, string GroupRef, string Name, string CurrencyCode, string CountryCode,
    DateOnly? PurchaseDate, decimal? PurchasePrice, decimal OriginalLoanAmount, bool HasWithdrawalFacility,
    bool IncludeInHousehold, bool IsRetired);
internal sealed record AssociationRow(string TargetType, string TargetRef, string? SnapshotRef, string Role,
    string PersonRef, int? ShareBasisPoints);
internal sealed record SnapshotRow(string SnapshotRef, string HouseholdRef, DateOnly SnapshotDate, DatePrecision DatePrecision);
internal sealed record ItemValueRow(string SnapshotRef, string ItemRef, string? Name, decimal? Amount,
    SnapshotValueOrigin ValueOrigin, DateOnly? LastConfirmedDate, DatePrecision? LastConfirmedPrecision, string? CarryFromSnapshotRef);
internal sealed record AssetValueRow(string SnapshotRef, string AssetRef, FinancedAssetBalanceKind BalanceKind, decimal? Amount,
    SnapshotValueOrigin ValueOrigin, DateOnly? LastConfirmedDate, DatePrecision? LastConfirmedPrecision, string? CarryFromSnapshotRef);

internal sealed record ExpectedTotals(string SnapshotRef, string CurrencyCode, decimal AllRecorded, decimal Household,
    decimal Excluded, Dictionary<string, decimal> Individuals, Dictionary<string, decimal> HouseholdByPerson,
    Dictionary<string, decimal> HouseholdByCategory, Dictionary<string, decimal> HouseholdByGroup, int CarriedContributions);

// Reject values that decimal.Parse would silently round. The prepared file uses
// plain invariant decimals; no binary floating point or scientific notation.
internal sealed class ExactDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        string text;
        if (reader.TokenType == JsonTokenType.String) text = reader.GetString()!;
        else
        {
            using var literal = JsonDocument.ParseValue(ref reader);
            text = literal.RootElement.GetRawText();
        }
        var parts = text.Split('.');
        if (parts.Length > 2 || parts.Any(part => part.Length == 0)
            || text.TrimStart('-').Any(c => c != '.' && !char.IsAsciiDigit(c)))
            throw new JsonException("Use a plain invariant decimal amount.");
        var scale = parts.Length == 2 ? parts[1].Length : 0;
        if (!BigInteger.TryParse(string.Concat(parts), out var coefficient)) throw new JsonException("Invalid decimal amount.");
        while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        if (scale > 28 || BigInteger.Abs(coefficient) > (BigInteger.One << 96) - 1
            || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount))
            throw new JsonException("Amount cannot be represented exactly as a .NET decimal.");
        return amount;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}
