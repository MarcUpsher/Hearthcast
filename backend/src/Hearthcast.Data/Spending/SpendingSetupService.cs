using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Persistence.Configurations;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Spending;

public sealed class SpendingSetupService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    public Task<ExpenseCategory> CreateCategoryAsync(Guid householdId, string name,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var household = await FindHouseholdAsync(context, householdId, token);
            var category = new ExpenseCategory(household, name);
            context.ExpenseCategories.Add(category);
            return category;
        }, cancellationToken);

    public Task<Merchant> CreateMerchantAsync(Guid householdId, string name,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var household = await FindHouseholdAsync(context, householdId, token);
            var merchant = new Merchant(household, name);
            context.Merchants.Add(merchant);
            return merchant;
        }, cancellationToken);

    public Task<SpendingItem> CreateItemAsync(Guid householdId, Guid expenseCategoryId, string code,
        string name, string unitKey, string unitLabel, string? notes = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var category = await FindCategoryAsync(context, householdId, expenseCategoryId, token);
            var item = new SpendingItem(category, code, name, unitKey, unitLabel, notes);
            if (await context.SpendingItems.AnyAsync(i => i.HouseholdId == householdId && i.Code == item.Code, token))
                throw new BusinessRuleViolationException("An item with this code already exists in this household, including retired items.");
            context.SpendingItems.Add(item);
            return item;
        }, cancellationToken);

    public Task<ExpenseCategory> RenameCategoryAsync(Guid householdId, Guid categoryId, string name,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var category = await FindCategoryAsync(context, householdId, categoryId, token);
            category.Rename(name);
            return category;
        }, cancellationToken);

    public Task<Merchant> RenameMerchantAsync(Guid householdId, Guid merchantId, string name,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var merchant = await FindMerchantAsync(context, householdId, merchantId, token);
            merchant.Rename(name);
            return merchant;
        }, cancellationToken);

    public Task<SpendingItem> UpdateItemAsync(Guid householdId, Guid itemId, Guid expenseCategoryId,
        string name, string unitLabel, string? notes, CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var item = await FindItemAsync(context, householdId, itemId, token);
            var category = await FindCategoryAsync(context, householdId, expenseCategoryId, token);
            item.Rename(name);
            item.SetCategory(category);
            item.SetUnitLabel(unitLabel);
            item.SetNotes(notes);
            return item;
        }, cancellationToken);

    public Task<ExpenseCategory> SetCategoryRetiredAsync(Guid householdId, Guid categoryId, bool isRetired,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var category = await FindCategoryAsync(context, householdId, categoryId, token);
            category.SetRetired(isRetired);
            return category;
        }, cancellationToken);

    public Task<Merchant> SetMerchantRetiredAsync(Guid householdId, Guid merchantId, bool isRetired,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var merchant = await FindMerchantAsync(context, householdId, merchantId, token);
            merchant.SetRetired(isRetired);
            return merchant;
        }, cancellationToken);

    public Task<SpendingItem> SetItemRetiredAsync(Guid householdId, Guid itemId, bool isRetired,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var item = await FindItemAsync(context, householdId, itemId, token);
            item.SetRetired(isRetired);
            return item;
        }, cancellationToken);

    public async Task<IReadOnlyList<ExpenseCategory>> ListCategoriesAsync(Guid householdId,
        bool includeRetired = true, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await FindHouseholdAsync(context, householdId, cancellationToken);
        return await context.ExpenseCategories.AsNoTracking()
            .Where(c => c.HouseholdId == householdId && (includeRetired || !c.IsRetired))
            .OrderBy(c => c.Name).ThenBy(c => c.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Merchant>> ListMerchantsAsync(Guid householdId,
        bool includeRetired = true, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await FindHouseholdAsync(context, householdId, cancellationToken);
        return await context.Merchants.AsNoTracking()
            .Where(m => m.HouseholdId == householdId && (includeRetired || !m.IsRetired))
            .OrderBy(m => m.Name).ThenBy(m => m.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpendingItem>> ListItemsAsync(Guid householdId,
        bool includeRetired = true, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await FindHouseholdAsync(context, householdId, cancellationToken);
        return await context.SpendingItems.AsNoTracking()
            .Where(i => i.HouseholdId == householdId && (includeRetired || !i.IsRetired))
            .OrderBy(i => i.Name).ThenBy(i => i.Code).ThenBy(i => i.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<ExpenseCategory> GetCategoryAsync(Guid householdId, Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindCategoryAsync(context, householdId, categoryId, cancellationToken);
    }

    public async Task<Merchant> GetMerchantAsync(Guid householdId, Guid merchantId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindMerchantAsync(context, householdId, merchantId, cancellationToken);
    }

    public async Task<SpendingItem> GetItemAsync(Guid householdId, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindItemAsync(context, householdId, itemId, cancellationToken);
    }

    public Task<SpendingItemMerchantPrice> CreateUsualPriceAsync(Guid householdId, Guid itemId,
        Guid merchantId, string currencyCode, decimal usualUnitPrice, CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var item = await FindItemAsync(context, householdId, itemId, token);
            var merchant = await FindMerchantAsync(context, householdId, merchantId, token);
            var price = new SpendingItemMerchantPrice(item, merchant, currencyCode, usualUnitPrice);
            if (await context.SpendingItemMerchantPrices.AnyAsync(p => p.HouseholdId == householdId
                && p.SpendingItemId == itemId && p.MerchantId == merchantId && p.CurrencyCode == price.CurrencyCode
                && p.UnitKey == item.UnitKey, token))
                throw new BusinessRuleViolationException("A usual price already exists for this item, merchant, currency and unit. Update it using its revision.");
            context.SpendingItemMerchantPrices.Add(price);
            return price;
        }, cancellationToken);

    public Task<SpendingItemMerchantPrice> UpdateUsualPriceAsync(Guid householdId, Guid priceId,
        Guid expectedRevision, decimal usualUnitPrice, CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var price = await FindPriceAsync(context, householdId, priceId, token);
            if (expectedRevision == Guid.Empty)
                throw new InvalidValueException("Supply the usual price revision.", nameof(expectedRevision));
            if (price.Revision != expectedRevision)
                throw new SpendingPriceConflictException();
            price.SetUsualUnitPrice(usualUnitPrice);
            return price;
        }, cancellationToken);

    public async Task<SpendingItemMerchantPrice> GetUsualPriceAsync(Guid householdId, Guid priceId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindPriceAsync(context, householdId, priceId, cancellationToken);
    }

    public async Task<IReadOnlyList<SpendingItemMerchantPrice>> ListUsualPricesAsync(Guid householdId, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await FindItemAsync(context, householdId, itemId, cancellationToken);
        return await context.SpendingItemMerchantPrices.AsNoTracking()
            .Where(p => p.HouseholdId == householdId && p.SpendingItemId == itemId)
            .OrderBy(p => p.CurrencyCode).ThenBy(p => p.MerchantId).ThenBy(p => p.Id).ToArrayAsync(cancellationToken);
    }

    public async Task<SpendingItemMerchantPrice?> FindUsualPriceAsync(Guid householdId, Guid itemId,
        Guid merchantId, string currencyCode, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var item = await FindItemAsync(context, householdId, itemId, cancellationToken);
        await FindMerchantAsync(context, householdId, merchantId, cancellationToken);
        var currency = RequiredAlphabeticCode.Normalize(currencyCode, 3);
        return await context.SpendingItemMerchantPrices.AsNoTracking().SingleOrDefaultAsync(p =>
            p.HouseholdId == householdId && p.SpendingItemId == itemId && p.MerchantId == merchantId
            && p.CurrencyCode == currency && p.UnitKey == item.UnitKey, cancellationToken);
    }

    private async Task<T> WriteAsync<T>(Func<HearthcastDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SerializableTransaction.ExecuteAsync(contextFactory, operation, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SpendingPriceConflictException(exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_SpendingItems_HouseholdId_Code" })
        {
            throw new BusinessRuleViolationException("An item with this code already exists in this household, including retired items.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: SpendingSetupConfigurationRules.UsualPriceIndex })
        {
            throw new BusinessRuleViolationException("A usual price already exists for this item, merchant, currency and unit. Update it using its revision.");
        }
    }

    private static async Task<Household> FindHouseholdAsync(HearthcastDbContext context, Guid householdId,
        CancellationToken token) =>
        await context.Households.SingleOrDefaultAsync(h => h.Id == householdId, token)
        ?? throw new ResourceNotFoundException("The household does not exist.");

    private static async Task<ExpenseCategory> FindCategoryAsync(HearthcastDbContext context, Guid householdId,
        Guid categoryId, CancellationToken token) =>
        await context.ExpenseCategories.SingleOrDefaultAsync(c => c.HouseholdId == householdId && c.Id == categoryId, token)
        ?? throw new ResourceNotFoundException("The expense category does not belong to this household.");

    private static async Task<Merchant> FindMerchantAsync(HearthcastDbContext context, Guid householdId,
        Guid merchantId, CancellationToken token) =>
        await context.Merchants.SingleOrDefaultAsync(m => m.HouseholdId == householdId && m.Id == merchantId, token)
        ?? throw new ResourceNotFoundException("The merchant does not belong to this household.");

    private static async Task<SpendingItem> FindItemAsync(HearthcastDbContext context, Guid householdId,
        Guid itemId, CancellationToken token) =>
        await context.SpendingItems.SingleOrDefaultAsync(i => i.HouseholdId == householdId && i.Id == itemId, token)
        ?? throw new ResourceNotFoundException("The spending item does not belong to this household.");

    private static async Task<SpendingItemMerchantPrice> FindPriceAsync(HearthcastDbContext context, Guid householdId,
        Guid priceId, CancellationToken token) =>
        await context.SpendingItemMerchantPrices.SingleOrDefaultAsync(p => p.HouseholdId == householdId && p.Id == priceId, token)
        ?? throw new ResourceNotFoundException("The usual price does not belong to this household.");
}
