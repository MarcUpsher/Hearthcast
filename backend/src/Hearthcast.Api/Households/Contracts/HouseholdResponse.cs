using Hearthcast.Data.Households;

namespace Hearthcast.Api.Households.Contracts;

public sealed record HouseholdResponse(Guid Id, string Name, IReadOnlyList<PersonResponse> People)
{
    internal static HouseholdResponse From(Household household) => new(household.Id, household.Name,
        household.People.OrderBy(person => person.DisplayName, StringComparer.Ordinal).ThenBy(person => person.Id)
            .Select(PersonResponse.From).ToArray());
}
