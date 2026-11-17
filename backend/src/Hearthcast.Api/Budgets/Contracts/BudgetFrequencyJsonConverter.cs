using System.Text.Json;
using System.Text.Json.Serialization;
using Hearthcast.Data.Budgets;

namespace Hearthcast.Api.Budgets.Contracts;

public sealed class BudgetFrequencyJsonConverter : JsonConverter<BudgetFrequency>
{
    public override BudgetFrequency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString()?.Trim();
            foreach (var name in Enum.GetNames<BudgetFrequency>())
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) return Enum.Parse<BudgetFrequency>(name);
        }
        throw new JsonException("Use Week, Month or Year.");
    }

    public override void Write(Utf8JsonWriter writer, BudgetFrequency value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Undefined budget frequency.");
        writer.WriteStringValue(value.ToString());
    }
}
