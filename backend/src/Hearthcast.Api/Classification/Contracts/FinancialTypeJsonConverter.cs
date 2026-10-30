using System.Text.Json.Serialization;
using Hearthcast.Data.Classification;

namespace Hearthcast.Api.Classification.Contracts;

// Keep the public contract explicit without changing serialization of other endpoints.
public sealed class FinancialTypeJsonConverter() : JsonStringEnumConverter<FinancialType>(allowIntegerValues: false);
