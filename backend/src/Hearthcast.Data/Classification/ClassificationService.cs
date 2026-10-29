using Hearthcast.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Classification;

public sealed class ClassificationService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public async Task<Category> CreateCategoryAsync(
        Guid householdId,
        string name,
        FinancialType financialType,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var household = await context.Households.SingleOrDefaultAsync(
            household => household.Id == householdId, cancellationToken)
            ?? throw new KeyNotFoundException("The household does not exist.");

        var category = new Category(household, name, financialType);
        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<ItemGroup> CreateGroupAsync(
        Guid householdId,
        Guid categoryId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var category = await context.Categories.SingleOrDefaultAsync(
            category => category.Id == categoryId && category.HouseholdId == householdId,
            cancellationToken)
            ?? throw new KeyNotFoundException("The category does not belong to this household.");

        var group = new ItemGroup(category, name);
        context.ItemGroups.Add(group);
        await context.SaveChangesAsync(cancellationToken);
        return group;
    }
}
