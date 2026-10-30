using Hearthcast.Data.Persistence;
using Hearthcast.Data.Validation;
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
            ?? throw new ResourceNotFoundException("The household does not exist.");

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
        var category = await FindCategoryAsync(context, householdId, categoryId, cancellationToken);

        var group = new ItemGroup(category, name);
        context.ItemGroups.Add(group);
        await context.SaveChangesAsync(cancellationToken);
        return group;
    }

    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(Guid householdId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Households.AnyAsync(household => household.Id == householdId, cancellationToken))
        {
            throw new ResourceNotFoundException("The household does not exist.");
        }
        return await context.Categories.AsNoTracking().Where(category => category.HouseholdId == householdId)
            .OrderBy(category => category.Name).ThenBy(category => category.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<Category> GetCategoryAsync(Guid householdId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindCategoryAsync(context, householdId, categoryId, cancellationToken);
    }

    public async Task<Category> RenameCategoryAsync(Guid householdId, Guid categoryId, string name,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var category = await FindCategoryAsync(context, householdId, categoryId, cancellationToken);
        category.Rename(name);
        await context.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<IReadOnlyList<ItemGroup>> ListGroupsAsync(Guid householdId, Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await FindCategoryAsync(context, householdId, categoryId, cancellationToken);
        return await context.ItemGroups.AsNoTracking()
            .Where(group => group.HouseholdId == householdId && group.CategoryId == categoryId)
            .OrderBy(group => group.Name).ThenBy(group => group.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<ItemGroup> GetGroupAsync(Guid householdId, Guid categoryId, Guid groupId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindGroupAsync(context, householdId, categoryId, groupId, cancellationToken);
    }

    public async Task<ItemGroup> RenameGroupAsync(Guid householdId, Guid categoryId, Guid groupId, string name,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var group = await FindGroupAsync(context, householdId, categoryId, groupId, cancellationToken);
        group.Rename(name);
        await context.SaveChangesAsync(cancellationToken);
        return group;
    }

    private static async Task<Category> FindCategoryAsync(HearthcastDbContext context, Guid householdId, Guid categoryId,
        CancellationToken cancellationToken) =>
        await context.Categories.SingleOrDefaultAsync(
            category => category.HouseholdId == householdId && category.Id == categoryId, cancellationToken)
        ?? throw new ResourceNotFoundException("The category does not belong to this household.");

    private static async Task<ItemGroup> FindGroupAsync(HearthcastDbContext context, Guid householdId, Guid categoryId,
        Guid groupId, CancellationToken cancellationToken) =>
        await context.ItemGroups.SingleOrDefaultAsync(
            group => group.HouseholdId == householdId && group.CategoryId == categoryId && group.Id == groupId, cancellationToken)
        ?? throw new ResourceNotFoundException("The group does not belong to this household and category.");

}
