using Hearthcast.Data.Budgets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Hearthcast.Data.Tests.Budgets;

public sealed class BudgetAllowanceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();
    private BudgetSetupService Setup => new(database);
    private BudgetSnapshotService Plans => new(database);
    private SpendingSetupService Spending => new(database);
    private static readonly DateOnly January = new(2026, 1, 1);
    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();
    private async Task<(Guid Home, ExpenseCategory Category)> Seed()
    {
        var home = await new HouseholdService(database).CreateAsync("Home", "Alex");
        return (home.Id, await Spending.CreateCategoryAsync(home.Id, "Food"));
    }
    private static BudgetEntryMetadataValues Metadata(BudgetEntry e) => new(e.ItemCode, e.ItemName, e.ExpenseCategoryId,
        e.ExpenseCategoryName, e.CurrencyCode, e.Frequency, e.IntervalCount, e.MerchantId, e.MerchantName, e.Notes, e.Kind);

    [Fact]
    public async Task MixedPlansCarryIndependentSavedKindsAmountsLabelsAndReadContracts()
    {
        var (home, category) = await Seed();
        var allowance = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month);
        var commitment = await Setup.CreateItemAsync(home, category.Id, "SUB", "Food subscription", "GBP", BudgetFrequency.Month);
        Assert.Equal(BudgetItemKind.Allowance, allowance.Kind); Assert.StartsWith("ALW_", allowance.Code);
        Assert.Equal("Food", allowance.Name); Assert.Null(allowance.MerchantId); Assert.Null(allowance.PaymentAnchorDate);
        var values = new BudgetSnapshotValues(January, [new(allowance.Id, BudgetEntryValueMode.Confirm, 400.123456m, January), new(commitment.Id, BudgetEntryValueMode.Confirm, 50, January)]);
        var original = (await Plans.CreateAsync(home, Guid.NewGuid(), values)).Snapshot;
        var copied = (await Plans.CreateAsync(home, Guid.NewGuid(), new(January.AddMonths(1), [new(allowance.Id, BudgetEntryValueMode.Carry), new(commitment.Id, BudgetEntryValueMode.Carry)], original.Id))).Snapshot;
        await Spending.RenameCategoryAsync(home, category.Id, "Groceries");
        await Setup.UpdateItemAsync(home, allowance.Id, category.Id, "Current allowance", "USD", BudgetFrequency.Week, 2, null, "New setup");
        await Setup.SetItemRetiredAsync(home, allowance.Id, true);
        await Plans.CorrectAmountAsync(home, original.Id, original.Revision, original.Entries.Single(e => e.Kind == BudgetItemKind.Allowance).Id, 10, January, null);
        var saved = await Plans.GetAsync(home, copied.Id);
        var a = saved.Entries.Single(e => e.Kind == BudgetItemKind.Allowance);
        Assert.Equal("Food", a.ExpenseCategoryName); Assert.Equal("GBP", a.CurrencyCode); Assert.Equal(400.123456m, a.PlannedAmount);
        Assert.Equal(January, a.LastConfirmedDate); Assert.Equal(BudgetValueOrigin.CarriedForward, a.ValueOrigin);
        Assert.Equal(copied.Revision, saved.Revision);
        Assert.Equal(2, (await Plans.ListAsync(home)).Records.Count);
        Assert.Equal(copied.Id, (await Plans.GetEffectiveAsync(home, January.AddMonths(2)))!.Snapshot.Id);
        Assert.Equal(copied.Id, (await Plans.FindCopySourceAsync(home, January.AddMonths(2)))!.Id);
        var current = await Plans.GetCurrentOutgoingsAsync(home, January.AddMonths(2));
        Assert.Equal(BudgetItemKind.Allowance, current.Items.Single(r => r.Item.Id == allowance.Id).Entry!.Kind);
        Assert.Equal("USD", current.Items.Single(r => r.Item.Id == allowance.Id).Item.CurrencyCode);
    }

    [Fact]
    public async Task ActiveAllowanceUniquenessIncludesCreateUpdateRestoreAndConcurrentRequests()
    {
        var (home, category) = await Seed();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month); return true; }
            catch (BusinessRuleViolationException) { return false; }
        }));
        Assert.Single(results, v => v); Assert.Single(await Setup.ListItemsAsync(home));
        var first = (await Setup.ListItemsAsync(home))[0]; await Setup.SetItemRetiredAsync(home, first.Id, true);
        var replacement = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Year);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Setup.SetItemRetiredAsync(home, first.Id, false));
        var usd = await Setup.CreateAllowanceAsync(home, category.Id, "USD", BudgetFrequency.Month);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Setup.UpdateItemAsync(home, usd.Id, category.Id, usd.Name, "GBP", BudgetFrequency.Week, 1, null, null));
        Assert.Equal("USD", (await Setup.GetItemAsync(home, usd.Id)).CurrencyCode);
        Assert.True((await Setup.GetItemAsync(home, first.Id)).IsRetired);
        Assert.False((await Setup.GetItemAsync(home, replacement.Id)).IsRetired);
    }

    [Fact]
    public async Task AllowanceCategoryMeaningCannotBeChangedByCarryOrMetadataOnlyAndCorrectionsDetachCopies()
    {
        var (home, category) = await Seed();
        var item = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month);
        var source = (await Plans.CreateAsync(home, Guid.NewGuid(), new(January, [new(item.Id, BudgetEntryValueMode.Confirm, 400, January)]))).Snapshot;
        var copied = (await Plans.CreateAsync(home, Guid.NewGuid(), new(January.AddMonths(1), [new(item.Id, BudgetEntryValueMode.Carry)], source.Id))).Snapshot;
        var nextCategory = await Spending.CreateCategoryAsync(home, "Pharmacy");
        var corrected = Metadata(source.Entries[0]) with { ExpenseCategoryId = nextCategory.Id, ExpenseCategoryName = nextCategory.Name };
        await Assert.ThrowsAsync<InvalidValueException>(() => Plans.CorrectEntryMetadataAsync(home, source.Id, source.Revision, source.Entries[0].Id, corrected));
        await Setup.UpdateItemAsync(home, item.Id, nextCategory.Id, item.Name, "GBP", BudgetFrequency.Month, 1, null, null);
        await Assert.ThrowsAsync<InvalidValueException>(() => Plans.CreateAsync(home, Guid.NewGuid(), new(January.AddMonths(2), [new(item.Id, BudgetEntryValueMode.Carry, RefreshFromSetup: true)], source.Id)));
        await Plans.CorrectEntryAsync(home, source.Id, source.Revision, source.Entries[0].Id, new(item.Id, BudgetEntryValueMode.Confirm, 20, January, Metadata: corrected));
        var saved = await Plans.GetAsync(home, copied.Id);
        Assert.Equal(400m, saved.Entries[0].PlannedAmount); Assert.Null(saved.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(category.Id, saved.Entries[0].ExpenseCategoryId); Assert.NotEqual(copied.Revision, saved.Revision);
    }

    [Fact]
    public async Task DuplicateAllowanceRowsCannotEnterThroughCreateAddOrCorrectionsAndFailuresAreAtomic()
    {
        var (home, category) = await Seed();
        var first = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month);
        await Setup.SetItemRetiredAsync(home, first.Id, true);
        var second = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month);
        var entries = new BudgetEntryValues[] { new(first.Id, BudgetEntryValueMode.Unrecorded), new(second.Id, BudgetEntryValueMode.Confirm, 0, January) };
        await Assert.ThrowsAsync<InvalidValueException>(() => Plans.CreateAsync(home, Guid.NewGuid(), new(January, entries)));
        var source = (await Plans.CreateAsync(home, Guid.NewGuid(), new(January, [entries[0]]))).Snapshot;
        await Assert.ThrowsAsync<InvalidValueException>(() => Plans.AddEntryAsync(home, source.Id, source.Revision, entries[1]));
        var other = await Spending.CreateCategoryAsync(home, "Other");
        var third = await Setup.CreateAllowanceAsync(home, other.Id, "GBP", BudgetFrequency.Month);
        source = await Plans.AddEntryAsync(home, source.Id, source.Revision, new(third.Id, BudgetEntryValueMode.Unrecorded));
        var thirdEntry = source.Entries.Single(e => e.BudgetItemId == third.Id);
        var clash = Metadata(thirdEntry) with { ExpenseCategoryId = category.Id, ExpenseCategoryName = category.Name };
        await Assert.ThrowsAsync<InvalidValueException>(() => Plans.CorrectEntryAsync(home, source.Id, source.Revision, thirdEntry.Id, new(third.Id, BudgetEntryValueMode.Unrecorded, Metadata: clash)));
        var saved = await Plans.GetAsync(home, source.Id); Assert.Equal(source.Revision, saved.Revision); Assert.Equal(2, saved.Entries.Count);
        Assert.Equal(other.Id, saved.Entries.Single(e => e.Id == thirdEntry.Id).ExpenseCategoryId);
    }

    [Fact]
    public async Task KindCannotBeSpoofedThroughMetadataAndAllowancesRejectProviderAndDates()
    {
        var (home, category) = await Seed();
        var item = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month);
        var source = (await Plans.CreateAsync(home, Guid.NewGuid(), new(January, [new(item.Id, BudgetEntryValueMode.Unrecorded)]))).Snapshot;
        var metadata = Metadata(source.Entries[0]) with { Kind = BudgetItemKind.Commitment };
        await Assert.ThrowsAsync<InvalidValueException>(() => Plans.CorrectEntryMetadataAsync(home, source.Id, source.Revision, source.Entries[0].Id, metadata));
        await Assert.ThrowsAsync<InvalidValueException>(() => Setup.UpdateItemAsync(home, item.Id, category.Id, item.Name, "GBP", BudgetFrequency.Month, 1, null, null, January));
        var merchant = await Spending.CreateMerchantAsync(home, "Shop");
        await Assert.ThrowsAsync<InvalidValueException>(() => Setup.UpdateItemAsync(home, item.Id, category.Id, item.Name, "GBP", BudgetFrequency.Month, 1, merchant.Id, null));
        var foreign = await Seed();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Setup.CreateAllowanceAsync(home, foreign.Category.Id, "GBP", BudgetFrequency.Month));
        Assert.Equal(BudgetItemKind.Allowance, (await Setup.GetItemAsync(home, item.Id)).Kind);
    }

    [Fact]
    public async Task ExplicitAllowanceMetadataRetriesReturnRetainedCorrectionsWithoutRewritingTheRequest()
    {
        var (home, category) = await Seed();
        var item = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month);
        var metadata = new BudgetEntryMetadataValues(item.Code, category.Name, category.Id, category.Name, "GBP", BudgetFrequency.Month, 1, Kind: BudgetItemKind.Allowance);
        var request = new BudgetSnapshotValues(January, [new(item.Id, BudgetEntryValueMode.Confirm, 400, January, Metadata: metadata)]);
        var original = (await Plans.CreateAsync(home, Guid.NewGuid(), request)).Snapshot;
        var corrected = await Plans.CorrectAmountAsync(home, original.Id, original.Revision, original.Entries[0].Id, 350, January, null);
        var retry = await Plans.CreateAsync(home, original.Id, request);
        Assert.False(retry.WasCreated); Assert.Equal(corrected.Revision, retry.Snapshot.Revision);
        Assert.Equal(350, retry.Snapshot.Entries[0].PlannedAmount); Assert.Equal(BudgetItemKind.Allowance, retry.Snapshot.Entries[0].Kind);
        Assert.Equal(original.CreationRequestHash, retry.Snapshot.CreationRequestHash);
    }

    [Fact]
    public async Task FilteredIndexesProtectAllowanceUniquenessEvenOutsideDomainWrites()
    {
        var (home, category) = await Seed();
        var first = await Setup.CreateItemAsync(home, category.Id, "FIRST", "First", "GBP", BudgetFrequency.Month);
        var second = await Setup.CreateItemAsync(home, category.Id, "SECOND", "Second", "GBP", BudgetFrequency.Month);
        await Plans.CreateAsync(home, Guid.NewGuid(), new(January, [new(first.Id, BudgetEntryValueMode.Unrecorded), new(second.Id, BudgetEntryValueMode.Unrecorded)]));
        await using var context = database.CreateDbContext();
        var setup = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE \"BudgetItems\" SET \"Kind\" = 2"));
        Assert.Equal("UX_BudgetItems_ActiveAllowance", setup.ConstraintName);
        var entries = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE \"BudgetEntries\" SET \"Kind\" = 2"));
        Assert.Equal("UX_BudgetEntries_Allowance", entries.ConstraintName);
    }

    [Fact]
    public async Task AdditiveMigrationPreservesLegacyPlansHashesAndTheirMatchingRetries()
    {
        var (home, category) = await Seed();
        var item = await Setup.CreateItemAsync(home, category.Id, "RENT", "Rent", "GBP", BudgetFrequency.Month);
        var request = new BudgetSnapshotValues(January, [new(item.Id, BudgetEntryValueMode.Confirm, 1000, January)]);
        var original = (await Plans.CreateAsync(home, Guid.NewGuid(), request)).Snapshot;
        await using (var context = database.CreateDbContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261119093542_AddBudgetPaymentAnchorDate"); await migrator.MigrateAsync();
        }
        var replay = await Plans.CreateAsync(home, original.Id, request);
        Assert.False(replay.WasCreated); Assert.Equivalent(original, replay.Snapshot);
        Assert.Equal(BudgetItemKind.Commitment, (await Setup.GetItemAsync(home, item.Id)).Kind);
        Assert.Equal(BudgetItemKind.Commitment, replay.Snapshot.Entries[0].Kind);
    }

    [Theory]
    [InlineData("UPDATE \"BudgetItems\" SET \"Kind\" = 9")]
    [InlineData("UPDATE \"BudgetEntries\" SET \"Kind\" = 0")]
    [InlineData("UPDATE \"BudgetItems\" SET \"PaymentAnchorDate\" = DATE '2026-01-01'")]
    [InlineData("UPDATE \"BudgetItems\" SET \"MerchantId\" = (SELECT \"Id\" FROM \"Merchants\" LIMIT 1)")]
    [InlineData("UPDATE \"BudgetEntries\" SET \"MerchantId\" = (SELECT \"Id\" FROM \"Merchants\" LIMIT 1), \"MerchantName\" = 'Shop'")]
    public async Task PersistenceRejectsInvalidAllowanceState(string sql)
    {
        var (home, category) = await Seed(); await Spending.CreateMerchantAsync(home, "Shop");
        var item = await Setup.CreateAllowanceAsync(home, category.Id, "GBP", BudgetFrequency.Month);
        await Plans.CreateAsync(home, Guid.NewGuid(), new(January, [new(item.Id, BudgetEntryValueMode.Unrecorded)]));
        await using var context = database.CreateDbContext();
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal("23514", error.SqlState);
    }
}
