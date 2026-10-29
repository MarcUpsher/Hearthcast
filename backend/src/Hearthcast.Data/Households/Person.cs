namespace Hearthcast.Data.Households;

public sealed class Person
{
    private Person()
    {
    }

    internal Person(Household household, string displayName, bool participatesInHouseholdFinances)
    {
        Id = Guid.NewGuid();
        Household = household;
        HouseholdId = household.Id;
        Rename(displayName);
        ParticipatesInHouseholdFinances = participatesInHouseholdFinances;
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Household Household { get; private set; } = null!;
    public string DisplayName { get; private set; } = string.Empty;
    public bool ParticipatesInHouseholdFinances { get; private set; }

    public void Rename(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }

    internal void SetParticipation(bool participatesInHouseholdFinances) =>
        ParticipatesInHouseholdFinances = participatesInHouseholdFinances;
}
