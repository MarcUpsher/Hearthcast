using System.Text.Json.Serialization;
using Hearthcast.Data.Classification;

namespace Hearthcast.Api.Classification.Contracts;

public sealed record CategoryResponse(Guid Id, Guid HouseholdId, string Name,
    [property: JsonConverter(typeof(FinancialTypeJsonConverter))] FinancialType FinancialType)
{
    internal static CategoryResponse From(Category category) =>
        new(category.Id, category.HouseholdId, category.Name, category.FinancialType);
}
