using Hearthcast.Data.Classification;

namespace Hearthcast.Api.Classification.Contracts;

public sealed record GroupResponse(Guid Id, Guid HouseholdId, Guid CategoryId, string Name)
{
    internal static GroupResponse From(ItemGroup group) => new(group.Id, group.HouseholdId, group.CategoryId, group.Name);
}
