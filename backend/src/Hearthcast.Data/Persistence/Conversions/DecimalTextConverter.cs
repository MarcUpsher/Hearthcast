using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hearthcast.Data.Persistence.Conversions;

public sealed class DecimalTextConverter() : ValueConverter<decimal, string>(
    value => value.ToString("G29", CultureInfo.InvariantCulture),
    value => decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
