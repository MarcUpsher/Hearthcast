using Hearthcast.Data.Households;

namespace Hearthcast.Api.Households.Contracts;

public sealed record PersonResponse(Guid Id, Guid HouseholdId, string DisplayName, bool ParticipatesInHouseholdFinances)
{
    internal static PersonResponse From(Person person) =>
        new(person.Id, person.HouseholdId, person.DisplayName, person.ParticipatesInHouseholdFinances);
}
