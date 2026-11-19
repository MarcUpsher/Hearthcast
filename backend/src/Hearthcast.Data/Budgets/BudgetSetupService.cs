using Hearthcast.Data.Persistence;
using Hearthcast.Data.Persistence.Configurations;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Budgets;

public sealed class BudgetSetupService(IDbContextFactory<HearthcastDbContext> contextFactory)
{
    private const string DuplicateCodeMessage = "A budget item with this code already exists in this household, including retired items.";

    public Task<BudgetItem> CreateItemAsync(Guid householdId, Guid expenseCategoryId, string code,
        string name, string currencyCode, BudgetFrequency frequency, int intervalCount = 1,
        Guid? merchantId = null, string? notes = null, CancellationToken cancellationToken = default) =>
        CreateItemAsync(householdId, expenseCategoryId, code, name, currencyCode, frequency, intervalCount,
            merchantId, notes, null, cancellationToken);

    public Task<BudgetItem> CreateItemAsync(Guid householdId, Guid expenseCategoryId, string code,
        string name, string currencyCode, BudgetFrequency frequency, int intervalCount, Guid? merchantId,
        string? notes, DateOnly? paymentAnchorDate, CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var category = await FindCategoryAsync(context, householdId, expenseCategoryId, token);
            var merchant = merchantId.HasValue ? await FindMerchantAsync(context, householdId, merchantId.Value, token) : null;
            var item = new BudgetItem(category, code, name, currencyCode, frequency, intervalCount, merchant, notes, paymentAnchorDate);
            if (await context.BudgetItems.AnyAsync(i => i.HouseholdId == householdId && i.Code == item.Code, token))
                throw new BusinessRuleViolationException(DuplicateCodeMessage);
            context.BudgetItems.Add(item);
            return item;
        }, cancellationToken);

    // Code and identity stay stable. Explicit nullable merchant/notes values permit clearing defaults.
    public Task<BudgetItem> UpdateItemAsync(Guid householdId, Guid itemId, Guid expenseCategoryId,
        string name, string currencyCode, BudgetFrequency frequency, int intervalCount, Guid? merchantId,
        string? notes, CancellationToken cancellationToken = default) =>
        UpdateItemCoreAsync(householdId, itemId, expenseCategoryId, name, currencyCode, frequency, intervalCount,
            merchantId, notes, false, null, cancellationToken);

    public Task<BudgetItem> UpdateItemAsync(Guid householdId, Guid itemId, Guid expenseCategoryId,
        string name, string currencyCode, BudgetFrequency frequency, int intervalCount, Guid? merchantId,
        string? notes, DateOnly? paymentAnchorDate, CancellationToken cancellationToken = default) =>
        UpdateItemCoreAsync(householdId, itemId, expenseCategoryId, name, currencyCode, frequency, intervalCount,
            merchantId, notes, true, paymentAnchorDate, cancellationToken);

    private Task<BudgetItem> UpdateItemCoreAsync(Guid householdId, Guid itemId, Guid expenseCategoryId,
        string name, string currencyCode, BudgetFrequency frequency, int intervalCount, Guid? merchantId,
        string? notes, bool changePaymentDate, DateOnly? paymentAnchorDate, CancellationToken cancellationToken) =>
        WriteAsync(async (context, token) =>
        {
            var item = await FindItemAsync(context, householdId, itemId, token);
            var category = await FindCategoryAsync(context, householdId, expenseCategoryId, token);
            var merchant = merchantId.HasValue ? await FindMerchantAsync(context, householdId, merchantId.Value, token) : null;
            item.Rename(name);
            item.SetCategory(category);
            item.SetMerchant(merchant);
            item.SetTerms(currencyCode, frequency, intervalCount);
            item.SetNotes(notes);
            if (changePaymentDate) item.SetPaymentAnchorDate(paymentAnchorDate);
            return item;
        }, cancellationToken);

    public Task<BudgetItem> SetItemRetiredAsync(Guid householdId, Guid itemId, bool isRetired,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async (context, token) =>
        {
            var item = await FindItemAsync(context, householdId, itemId, token);
            item.SetRetired(isRetired);
            return item;
        }, cancellationToken);

    public async Task<BudgetItem> GetItemAsync(Guid householdId, Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await FindItemAsync(context, householdId, itemId, cancellationToken);
    }

    public async Task<IReadOnlyList<BudgetItem>> ListItemsAsync(Guid householdId,
        bool includeRetired = true, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Households.AnyAsync(h => h.Id == householdId, cancellationToken))
            throw new ResourceNotFoundException("The household does not exist.");
        return await context.BudgetItems.AsNoTracking()
            .Where(i => i.HouseholdId == householdId && (includeRetired || !i.IsRetired))
            .OrderBy(i => i.Name).ThenBy(i => i.Code).ThenBy(i => i.Id).ToArrayAsync(cancellationToken);
    }

    private async Task<T> WriteAsync<T>(Func<HearthcastDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SerializableTransaction.ExecuteAsync(contextFactory, operation, cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: BudgetItemConfiguration.CodeIndexName })
        {
            throw new BusinessRuleViolationException(DuplicateCodeMessage);
        }
    }

    private static async Task<BudgetItem> FindItemAsync(HearthcastDbContext context, Guid householdId,
        Guid itemId, CancellationToken token) =>
        await context.BudgetItems.SingleOrDefaultAsync(i => i.HouseholdId == householdId && i.Id == itemId, token)
        ?? throw new ResourceNotFoundException("The budget item does not belong to this household.");

    private static async Task<ExpenseCategory> FindCategoryAsync(HearthcastDbContext context, Guid householdId,
        Guid categoryId, CancellationToken token) =>
        await context.ExpenseCategories.SingleOrDefaultAsync(c => c.HouseholdId == householdId && c.Id == categoryId, token)
        ?? throw new ResourceNotFoundException("The expense category does not belong to this household.");

    private static async Task<Merchant> FindMerchantAsync(HearthcastDbContext context, Guid householdId,
        Guid merchantId, CancellationToken token) =>
        await context.Merchants.SingleOrDefaultAsync(m => m.HouseholdId == householdId && m.Id == merchantId, token)
        ?? throw new ResourceNotFoundException("The merchant does not belong to this household.");
}
