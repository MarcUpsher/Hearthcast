using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Classification;

public sealed class ItemGroup
{
    private ItemGroup()
    {
    }

    internal ItemGroup(Category category, string name)
    {
        Id = Guid.NewGuid();
        Category = category;
        CategoryId = category.Id;
        HouseholdId = category.HouseholdId;
        Rename(name);
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;

    public void Rename(string name)
    {
        Name = RequiredText.Trim(name);
    }
}
