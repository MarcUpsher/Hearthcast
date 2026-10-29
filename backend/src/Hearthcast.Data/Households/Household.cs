using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Households;

public sealed class Household
{
    private readonly List<Person> _people = [];

    private Household()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<Person> People => _people.AsReadOnly();

    public void Rename(string name)
    {
        Name = RequiredText.Trim(name);
    }

    internal static Household Create(string name, string firstPersonDisplayName)
    {
        var household = new Household { Id = Guid.NewGuid() };
        household.Rename(name);
        household.AddPerson(firstPersonDisplayName, participatesInHouseholdFinances: true);
        return household;
    }

    internal void AddPerson(string displayName, bool participatesInHouseholdFinances)
    {
        _people.Add(new Person(this, displayName, participatesInHouseholdFinances));
    }

    internal void SetPersonParticipation(Guid personId, bool participatesInHouseholdFinances)
    {
        var person = FindPerson(personId);
        if (!participatesInHouseholdFinances)
        {
            EnsureAnotherParticipant(person);
        }

        person.SetParticipation(participatesInHouseholdFinances);
    }

    internal Person RemovePerson(Guid personId)
    {
        var person = FindPerson(personId);
        if (_people.Count == 1)
        {
            throw new InvalidOperationException("A household must contain at least one person.");
        }

        EnsureAnotherParticipant(person);
        _people.Remove(person);
        return person;
    }

    private Person FindPerson(Guid personId) =>
        _people.SingleOrDefault(person => person.Id == personId)
        ?? throw new KeyNotFoundException("The person does not belong to this household.");

    private void EnsureAnotherParticipant(Person person)
    {
        if (person.ParticipatesInHouseholdFinances
            && !_people.Any(other => other.Id != person.Id && other.ParticipatesInHouseholdFinances))
        {
            throw new InvalidOperationException(
                "A household must have at least one person participating in its finances.");
        }
    }
}
