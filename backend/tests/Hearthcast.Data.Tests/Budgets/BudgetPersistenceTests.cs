using Hearthcast.Data.Budgets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.Budgets;

public sealed class BudgetPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly CheckedOn = new(2025, 12, 28);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private sealed record Data(Household Home, ExpenseCategory Category, Merchant Merchant, BudgetItem Item, BudgetSnapshot Source)
    {
        internal BudgetEntryMetadata Metadata => BudgetEntryMetadata.FromSetup(Item, Category, Merchant);
    }

    private async Task<Data> SeedAsync()
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Utilities");
        var merchant = new Merchant(home, "Supplier");
        var item = new BudgetItem(category, "ENERGY", "Energy", "GBP", BudgetFrequency.Month, merchant: merchant, notes: "Usage");
        var source = new BudgetSnapshot(Guid.NewGuid(), home, January,
            [new(BudgetEntryMetadata.FromSetup(item, category, merchant), 200.123456m, CheckedOn)], new string('a', 64), notes: "Original");
        await using var context = _database.CreateDbContext();
        context.AddRange(home, category, merchant, item, source);
        await context.SaveChangesAsync();
        return new(home, category, merchant, item, source);
    }

    private async Task<BudgetSnapshot> CarryAsync(Data d)
    {
        var plan = new BudgetSnapshot(Guid.NewGuid(), d.Home, new(2026, 2, 15),
            [BudgetEntryInput.Carry(d.Source, d.Source.Entries[0])], new string('b', 64), d.Source);
        await using var context = _database.CreateDbContext();
        context.Add(plan);
        await context.SaveChangesAsync();
        return plan;
    }

    [Fact]
    public async Task PlansRoundTripHistoricalContextAndExactAmountsAfterSetupAndSourceChanges()
    {
        var d = await SeedAsync();
        var next = await CarryAsync(d);
        await using (var context = _database.CreateDbContext())
        {
            var item = await context.BudgetItems.SingleAsync();
            item.Rename("Renamed"); item.SetTerms("USD", BudgetFrequency.Year, 3); item.SetRetired(true);
            var other = new ExpenseCategory(d.Home, "New category");
            context.Add(other); item.SetCategory(other); item.SetMerchant(null);
            (await context.ExpenseCategories.SingleAsync()).Rename("New category name");
            (await context.Merchants.SingleAsync()).Rename("Renamed provider");
            var source = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id);
            source.CorrectAmount(source.Revision, source.Entries[0].Id, 20, new(2026, 2, 1), "Correction");
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        var entry = Assert.Single(saved.Entries);
        Assert.Equal("Energy", entry.ItemName);
        Assert.Equal("ENERGY", entry.ItemCode);
        Assert.Equal("Utilities", entry.ExpenseCategoryName);
        Assert.Equal("Supplier", entry.MerchantName);
        Assert.Equal(d.Category.Id, entry.ExpenseCategoryId);
        Assert.Equal(d.Merchant.Id, entry.MerchantId);
        Assert.Equal("GBP", entry.CurrencyCode);
        Assert.Equal(BudgetFrequency.Month, entry.Frequency);
        Assert.Equal(1, entry.IntervalCount);
        Assert.Equal("Usage", entry.Notes);
        Assert.Equal(200.123456m, entry.PlannedAmount);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal(d.Source.Entries[0].Id, entry.CarriedFromBudgetEntryId);
        Assert.Equal(next.CreationRequestHash, saved.CreationRequestHash);
        Assert.Equal(next.Revision, saved.Revision);
        Assert.True(saved.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataCorrectionRoundTripsLinkedAndDetachedCarryWithoutReadingTheChangedSource(bool detached)
    {
        var d = await SeedAsync();
        var next = await CarryAsync(d);
        await using (var context = _database.CreateDbContext())
        {
            var source = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id);
            source.CorrectAmount(source.Revision, source.Entries[0].Id, 250, new(2026, 2, 1), "Source correction");
            if (detached)
            {
                var receiver = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
                receiver.DetachSources(receiver.Revision, [receiver.Entries[0].Id]);
            }
            await context.SaveChangesAsync();
        }
        Guid priorRevision;
        var category = new ExpenseCategory(d.Home, "Bills");
        var merchant = new Merchant(d.Home, "Corrected supplier");
        await using (var context = _database.CreateDbContext())
        {
            // Only the receiver is loaded. A source lookup is unnecessary for a metadata edit.
            var receiver = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
            priorRevision = receiver.Revision;
            context.AddRange(category, merchant);
            receiver.CorrectEntryMetadata(receiver.Revision, receiver.Entries[0].Id,
                BudgetEntryMetadata.FromEntry(receiver.Entries[0]) with
                {
                    ItemName = "Historical description", ExpenseCategoryId = category.Id,
                    ExpenseCategoryName = category.Name, MerchantId = merchant.Id, MerchantName = merchant.Name,
                    Notes = "Metadata correction"
                });
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        var entry = saved.Entries[0];
        Assert.Equal(next.Entries[0].Id, entry.Id);
        Assert.Equal("Historical description", entry.ItemName);
        Assert.Equal(category.Id, entry.ExpenseCategoryId);
        Assert.Equal(merchant.Id, entry.MerchantId);
        Assert.Equal("Metadata correction", entry.Notes);
        Assert.Equal(200.123456m, entry.PlannedAmount);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal(detached ? (Guid?)null : d.Source.Entries[0].Id, entry.CarriedFromBudgetEntryId);
        Assert.NotEqual(priorRevision, saved.Revision);
        Assert.Equal(next.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Equal(next.CreationRequestHash, saved.CreationRequestHash);
        Assert.Equal(d.Source.Id, saved.CopiedFromBudgetSnapshotId);
    }

    [Fact]
    public async Task StaleMetadataCorrectionCannotOverwriteAConcurrentAmountCorrection()
    {
        var d = await SeedAsync();
        var next = await CarryAsync(d);
        await using var first = _database.CreateDbContext();
        await using var second = _database.CreateDbContext();
        var winner = await first.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        var loser = await second.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        winner.CorrectAmount(winner.Revision, winner.Entries[0].Id, 20, new(2026, 2, 1), "Confirmed");
        loser.CorrectEntryMetadata(loser.Revision, loser.Entries[0].Id,
            BudgetEntryMetadata.FromEntry(loser.Entries[0]) with { ItemName = "Stale description" });
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        Assert.Equal(20, saved.Entries[0].PlannedAmount);
        Assert.Equal("Energy", saved.Entries[0].ItemName);
        Assert.Equal(BudgetValueOrigin.Confirmed, saved.Entries[0].ValueOrigin);
        Assert.Null(saved.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(winner.Revision, saved.Revision);
    }

    [Fact]
    public async Task HistoricalCurrencyCanBeCarriedAndCorrectedButCannotBeSelectedAsANewCurrency()
    {
        var d = await SeedAsync();
        await using (var context = _database.CreateDbContext())
        {
            // Synthetic saved code stands in for a formerly supported currency removed from the list.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "BudgetEntries" SET "CurrencyCode" = {"ZZZ"} WHERE "BudgetSnapshotId" = {d.Source.Id}
                """);
        }
        await using var edit = _database.CreateDbContext();
        var source = await edit.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id);
        var historical = BudgetEntryMetadata.FromEntry(source.Entries[0]);
        Assert.Throws<InvalidValueException>(() => new BudgetItem(d.Category, "NEW", "New", "ZZZ", BudgetFrequency.Month));
        Assert.Throws<InvalidValueException>(() => new BudgetSnapshot(Guid.NewGuid(), d.Home, new(2026, 2, 1),
            [new(historical, 200, CheckedOn)], new string('b', 64)));
        var next = new BudgetSnapshot(Guid.NewGuid(), d.Home, new(2026, 2, 1),
            [BudgetEntryInput.Carry(source, source.Entries[0])], new string('b', 64), source);
        edit.Add(next); await edit.SaveChangesAsync();
        next.CorrectEntryMetadata(next.Revision, next.Entries[0].Id,
            historical with { ItemName = "Legacy description", CurrencyCode = " zzz " });
        await edit.SaveChangesAsync();
        Assert.Equal(BudgetValueOrigin.CarriedForward, next.Entries[0].ValueOrigin);
        Assert.Equal(200.123456m, next.Entries[0].PlannedAmount);
        Assert.Equal(source.Entries[0].Id, next.Entries[0].CarriedFromBudgetEntryId);
        // Explicit re-confirmation may retain the saved currency; selecting another unknown code may not.
        next.CorrectEntry(next.Revision, next.Entries[0].Id, new(historical, 210, new(2026, 2, 2)));
        await edit.SaveChangesAsync();
        var revision = next.Revision;
        Assert.Throws<InvalidValueException>(() => next.CorrectEntry(revision, next.Entries[0].Id,
            new(historical with { CurrencyCode = "YYY" }, 210, CheckedOn)));
        Assert.Throws<InvalidValueException>(() => next.CorrectEntry(revision, next.Entries[0].Id,
            new(historical with { BudgetItemId = Guid.NewGuid() }, 210, CheckedOn)));
        Assert.Throws<InvalidValueException>(() => next.CorrectEntryMetadata(revision, next.Entries[0].Id,
            historical with { CurrencyCode = "12A" }));
        Assert.Equal(revision, next.Revision);
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        Assert.Equal("ZZZ", saved.Entries[0].CurrencyCode);
        Assert.Equal(210, saved.Entries[0].PlannedAmount);
        Assert.Equal(BudgetValueOrigin.Confirmed, saved.Entries[0].ValueOrigin);
        Assert.Null(saved.Entries[0].CarriedFromBudgetEntryId);
    }

    [Fact]
    public async Task SavedEntriesAllowAdditiveCategoryAllowancesMixedCurrenciesAndNullVersusZero()
    {
        var d = await SeedAsync();
        var allowance = new BudgetItem(d.Category, "ALLOWANCE", "Allowance", "GBP", BudgetFrequency.Week);
        var foreign = new BudgetItem(d.Category, "FOREIGN", "Other allowance", "ZAR", BudgetFrequency.Year);
        var absent = new BudgetItem(d.Category, "ABSENT", "Not included", "GBP", BudgetFrequency.Month);
        var plan = new BudgetSnapshot(Guid.NewGuid(), d.Home, new(2026, 2, 1),
            [new(d.Metadata, 0, CheckedOn), new(BudgetEntryMetadata.FromSetup(allowance, d.Category)),
             new(BudgetEntryMetadata.FromSetup(foreign, d.Category), 0.000001m, CheckedOn)], new string('b', 64));
        var empty = new BudgetSnapshot(Guid.NewGuid(), d.Home, new(2026, 3, 1), [], new string('c', 64));
        await using (var context = _database.CreateDbContext())
        {
            context.AddRange(allowance, foreign, absent, plan, empty);
            await context.SaveChangesAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == plan.Id);
        Assert.Equal(3, saved.Entries.Count);
        Assert.False(saved.IsComplete);
        Assert.Equal(0, saved.Entries.Single(e => e.BudgetItemId == d.Item.Id).PlannedAmount);
        Assert.Null(saved.Entries.Single(e => e.BudgetItemId == allowance.Id).PlannedAmount);
        Assert.DoesNotContain(saved.Entries, e => e.BudgetItemId == absent.Id);
        Assert.Equal(new[] { "GBP", "ZAR" }, saved.Entries.Select(e => e.CurrencyCode).Distinct().Order());
        var savedEmpty = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == empty.Id);
        Assert.True(savedEmpty.IsComplete);
        Assert.Empty(savedEmpty.Entries);
    }

    [Fact]
    public async Task EffectivePlanSelectionSupportsUnknownFutureAndBackdatedReplacementIntervals()
    {
        var d = await SeedAsync();
        var future = new BudgetSnapshot(Guid.NewGuid(), d.Home, new(2027, 1, 1), [], new string('b', 64));
        var backdated = new BudgetSnapshot(Guid.NewGuid(), d.Home, new(2026, 2, 15), [], new string('c', 64));
        await using var context = _database.CreateDbContext();
        context.AddRange(future, backdated); await context.SaveChangesAsync();
        async Task<Guid?> At(DateOnly date) => await context.BudgetSnapshots.Where(s => s.HouseholdId == d.Home.Id && s.EffectiveFrom <= date)
            .OrderByDescending(s => s.EffectiveFrom).Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
        Assert.Null(await At(January.AddDays(-1)));
        Assert.Equal(d.Source.Id, await At(January));
        Assert.Equal(d.Source.Id, await At(new(2026, 2, 14)));
        Assert.Equal(backdated.Id, await At(new(2026, 2, 15)));
        Assert.Equal(backdated.Id, await At(new(2026, 12, 31)));
        Assert.Equal(future.Id, await At(new(2027, 1, 1)));
    }

    [Fact]
    public async Task EntryCorrectionsAreProtectedByTheParentRevisionAndRollbackLosingChildChanges()
    {
        var d = await SeedAsync();
        await using var first = _database.CreateDbContext();
        await using var second = _database.CreateDbContext();
        var winner = await first.BudgetSnapshots.Include(s => s.Entries).SingleAsync();
        var loser = await second.BudgetSnapshots.Include(s => s.Entries).SingleAsync();
        winner.CorrectAmount(winner.Revision, winner.Entries[0].Id, 20, new(2026, 2, 1), "Winner");
        loser.CorrectAmount(loser.Revision, loser.Entries[0].Id, 10, new(2026, 2, 1), "Loser");
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync();
        Assert.Equal(20, saved.Entries[0].PlannedAmount);
        Assert.Equal("Winner", saved.Entries[0].Notes);
        Assert.Equal(winner.Revision, saved.Revision);
        Assert.Equal(d.Source.CreationRequestHash, saved.CreationRequestHash);
    }

    [Fact]
    public async Task RemovingASourceEntryRequiresTransactionalDetachmentAndPreservesTheIndependentDescendant()
    {
        var d = await SeedAsync();
        var next = await CarryAsync(d);
        await using (var context = _database.CreateDbContext())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "BudgetEntries" WHERE "Id" = {d.Source.Entries[0].Id}"""));
            Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.RestrictViolation });
        }
        await using (var context = _database.CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var receiver = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
            var source = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id);
            receiver.DetachSources(receiver.Revision, [receiver.Entries[0].Id]);
            await context.SaveChangesAsync(); // Clear the restrictive FK inside the correction transaction.
            context.BudgetEntries.Remove(source.RemoveEntry(source.Revision, source.Entries[0].Id));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        var entry = Assert.Single(saved.Entries);
        Assert.Null(entry.CarriedFromBudgetEntryId);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal(200.123456m, entry.PlannedAmount);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.NotEqual(next.Revision, saved.Revision);
        Assert.Equal(next.CreationRequestHash, saved.CreationRequestHash);
        Assert.Equal(d.Source.Id, saved.CopiedFromBudgetSnapshotId);
        Assert.Empty((await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id)).Entries);
        var deleteError = await Assert.ThrowsAsync<PostgresException>(() => read.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "BudgetSnapshots" WHERE "Id" = {d.Source.Id}"""));
        Assert.Contains(deleteError.SqlState, new[] { PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.RestrictViolation });
    }

    [Fact]
    public async Task AConcurrentDescendantEditRollsBackSourceCorrectionAndEarlierDetachment()
    {
        var d = await SeedAsync();
        var next = await CarryAsync(d);
        await using var correction = _database.CreateDbContext();
        var receiver = await correction.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        var source = await correction.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id);
        await using (var edit = _database.CreateDbContext())
        {
            var changed = await edit.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
            changed.CorrectHeader(changed.Revision, changed.EffectiveFrom, "Concurrent notes");
            await edit.SaveChangesAsync();
        }
        await using (var transaction = await correction.Database.BeginTransactionAsync())
        {
            source.CorrectAmount(source.Revision, source.Entries[0].Id, 20, new(2026, 2, 1), null);
            await correction.SaveChangesAsync();
            receiver.DetachSources(receiver.Revision, [receiver.Entries[0].Id], true);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => correction.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await using var read = _database.CreateDbContext();
        var savedSource = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id);
        var savedReceiver = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        Assert.Equal(d.Source.Revision, savedSource.Revision);
        Assert.Equal(200.123456m, savedSource.Entries[0].PlannedAmount);
        Assert.Equal("Concurrent notes", savedReceiver.Notes);
        Assert.Equal(d.Source.Id, savedReceiver.CopiedFromBudgetSnapshotId);
        Assert.Equal(d.Source.Entries[0].Id, savedReceiver.Entries[0].CarriedFromBudgetEntryId);
    }

    [Fact]
    public async Task DateCorrectionCanDetachInvalidIncomingLinksWithoutRewritingTheReceivingPlan()
    {
        var d = await SeedAsync();
        var next = await CarryAsync(d);
        await using (var context = _database.CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var receiver = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
            var source = await context.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == d.Source.Id);
            receiver.DetachSources(receiver.Revision, [receiver.Entries[0].Id], true);
            source.CorrectHeader(source.Revision, new(2026, 3, 1), "Date corrected");
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var read = _database.CreateDbContext();
        var saved = await read.BudgetSnapshots.Include(s => s.Entries).SingleAsync(s => s.Id == next.Id);
        Assert.Equal(next.EffectiveFrom, saved.EffectiveFrom);
        Assert.Null(saved.CopiedFromBudgetSnapshotId);
        Assert.Null(saved.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(200.123456m, saved.Entries[0].PlannedAmount);
        Assert.Equal(BudgetValueOrigin.CarriedForward, saved.Entries[0].ValueOrigin);
        Assert.NotEqual(next.Revision, saved.Revision);
        Assert.Equal(next.CreationRequestHash, saved.CreationRequestHash);
    }

    [Theory]
    [InlineData("itemCategory")]
    [InlineData("itemMerchant")]
    [InlineData("entryItem")]
    [InlineData("entryCategory")]
    [InlineData("entryMerchant")]
    [InlineData("entryPlan")]
    [InlineData("entrySource")]
    [InlineData("template")]
    public async Task CompositeForeignKeysRejectCrossHouseholdReferences(string target)
    {
        var d = await SeedAsync();
        var foreign = await SeedAsync();
        await using var context = _database.CreateDbContext();
        FormattableString sql = target switch
        {
            "itemCategory" => $"""UPDATE "BudgetItems" SET "ExpenseCategoryId" = {foreign.Category.Id} WHERE "Id" = {d.Item.Id}""",
            "itemMerchant" => $"""UPDATE "BudgetItems" SET "MerchantId" = {foreign.Merchant.Id} WHERE "Id" = {d.Item.Id}""",
            "entryItem" => $"""UPDATE "BudgetEntries" SET "BudgetItemId" = {foreign.Item.Id} WHERE "BudgetSnapshotId" = {d.Source.Id}""",
            "entryCategory" => $"""UPDATE "BudgetEntries" SET "ExpenseCategoryId" = {foreign.Category.Id} WHERE "BudgetSnapshotId" = {d.Source.Id}""",
            "entryMerchant" => $"""UPDATE "BudgetEntries" SET "MerchantId" = {foreign.Merchant.Id} WHERE "BudgetSnapshotId" = {d.Source.Id}""",
            "entryPlan" => $"""UPDATE "BudgetEntries" SET "BudgetSnapshotId" = {foreign.Source.Id} WHERE "BudgetSnapshotId" = {d.Source.Id}""",
            "entrySource" => $"""UPDATE "BudgetEntries" SET "ValueOrigin" = {3}, "CarriedFromBudgetEntryId" = {foreign.Source.Entries[0].Id} WHERE "BudgetSnapshotId" = {d.Source.Id}""",
            _ => $"""UPDATE "BudgetSnapshots" SET "CopiedFromBudgetSnapshotId" = {foreign.Source.Id} WHERE "Id" = {d.Source.Id}"""
        };
        // Values remain parameters; table and column names are fixed by the selected statement.
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(sql));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Theory]
    [InlineData("-0.000001")]
    [InlineData("1000000000000")]
    [InlineData("0.0000001")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task DatabaseRejectsInvalidExactAmountsWithoutNumericCoercion(string value)
    {
        var d = await SeedAsync();
        await using var context = _database.CreateDbContext();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "BudgetEntries" SET "PlannedAmount" = CAST({value} AS numeric) WHERE "BudgetSnapshotId" = {d.Source.Id}
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal(200.123456m, (await context.BudgetEntries.AsNoTracking().SingleAsync()).PlannedAmount);
    }

    [Theory]
    [InlineData("BudgetItems", "\"Code\" = 'lowercase'")]
    [InlineData("BudgetItems", "\"Name\" = U&'\\00A0name'")]
    [InlineData("BudgetItems", "\"Frequency\" = 0")]
    [InlineData("BudgetItems", "\"IntervalCount\" = 0")]
    [InlineData("BudgetItems", "\"CurrencyCode\" = 'gbp'")]
    [InlineData("BudgetItems", "\"Notes\" = ''")]
    [InlineData("BudgetSnapshots", "\"EffectiveFrom\" = '-infinity'")]
    [InlineData("BudgetSnapshots", "\"EffectiveFrom\" = 'infinity'")]
    [InlineData("BudgetSnapshots", "\"Revision\" = '00000000-0000-0000-0000-000000000000'")]
    [InlineData("BudgetSnapshots", "\"CreationRequestHash\" = 'invalid'")]
    [InlineData("BudgetSnapshots", "\"UpdatedAtUtc\" = \"CreatedAtUtc\" - interval '1 second'")]
    [InlineData("BudgetSnapshots", "\"CreatedAtUtc\" = '-infinity'")]
    [InlineData("BudgetSnapshots", "\"CopiedFromBudgetSnapshotId\" = \"Id\"")]
    [InlineData("BudgetEntries", "\"ValueOrigin\" = 0")]
    [InlineData("BudgetEntries", "\"ValueOrigin\" = 1")]
    [InlineData("BudgetEntries", "\"PlannedAmount\" = NULL")]
    [InlineData("BudgetEntries", "\"LastConfirmedDate\" = NULL")]
    [InlineData("BudgetEntries", "\"LastConfirmedDate\" = 'infinity'")]
    [InlineData("BudgetEntries", "\"ValueOrigin\" = 3, \"PlannedAmount\" = NULL")]
    [InlineData("BudgetEntries", "\"CarriedFromBudgetEntryId\" = \"Id\", \"ValueOrigin\" = 3")]
    [InlineData("BudgetEntries", "\"MerchantId\" = NULL")]
    [InlineData("BudgetEntries", "\"MerchantName\" = NULL")]
    [InlineData("BudgetEntries", "\"ItemCode\" = 'lowercase'")]
    [InlineData("BudgetEntries", "\"ItemName\" = U&'name\\2000'")]
    [InlineData("BudgetEntries", "\"ExpenseCategoryName\" = ''")]
    [InlineData("BudgetEntries", "\"Frequency\" = 4")]
    [InlineData("BudgetEntries", "\"IntervalCount\" = -1")]
    [InlineData("BudgetEntries", "\"CurrencyCode\" = '12A'")]
    public async Task DatabaseEnforcesRowLocalRulesAgainstDirectSql(string table, string assignment)
    {
        await SeedAsync();
        await using var context = _database.CreateDbContext();
        // Trusted, fixed SQL fragments from InlineData, never application input.
#pragma warning disable EF1002 // Table/assignment fragments are fixed test cases above.
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET {assignment}"));
#pragma warning restore EF1002
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    [Fact]
    public async Task CodesPlanDatesAndPlanItemsHaveSeparateHouseholdScopedUniquenessRules()
    {
        var d = await SeedAsync();
        await SeedAsync(); // Identical codes/dates in another household are valid.
        var sameCode = new BudgetItem(d.Category, " energy ", "Another", "GBP", BudgetFrequency.Month);
        await using (var context = _database.CreateDbContext())
        {
            context.Add(sameCode);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
        }
        await using (var context = _database.CreateDbContext())
        {
            context.Add(new BudgetSnapshot(Guid.NewGuid(), d.Home, January, [], new string('b', 64)));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ((PostgresException)error.InnerException!).SqlState);
        }
        await using var sql = _database.CreateDbContext();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => sql.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "BudgetEntries" ("Id", "HouseholdId", "BudgetSnapshotId", "BudgetItemId", "ItemCode", "ItemName",
                "ExpenseCategoryId", "ExpenseCategoryName", "CurrencyCode", "Frequency", "IntervalCount", "ValueOrigin")
            SELECT {Guid.NewGuid()}, "HouseholdId", "BudgetSnapshotId", "BudgetItemId", "ItemCode", "ItemName",
                "ExpenseCategoryId", "ExpenseCategoryName", "CurrencyCode", "Frequency", "IntervalCount", 1
            FROM "BudgetEntries" WHERE "BudgetSnapshotId" = {d.Source.Id}
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    [Theory]
    [InlineData("BudgetItems")]
    [InlineData("ExpenseCategories")]
    [InlineData("Merchants")]
    public async Task ReferencedSetupCannotBeDeleted(string table)
    {
        await SeedAsync();
        await using var context = _database.CreateDbContext();
        var sql = table switch
        {
            "BudgetItems" => "DELETE FROM \"BudgetItems\"",
            "ExpenseCategories" => "DELETE FROM \"ExpenseCategories\"",
            "Merchants" => "DELETE FROM \"Merchants\"",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(error.SqlState, new[] { PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.RestrictViolation });
    }

    [Fact]
    public async Task MigrationPreservesSpendingDataAndCanRollBackAndReapplyWithoutModelDrift()
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Food");
        var merchant = new Merchant(home, "Shop");
        var receipt = new SpendingRecord(Guid.NewGuid(), merchant, January, "GBP", [new(category, 12.50m)], new string('c', 64));
        await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117130910_AddSpendingRecords");
        context.AddRange(home, category, merchant, receipt); await context.SaveChangesAsync();
        await migrator.MigrateAsync();
        var d = await SeedAsync();
        await CarryAsync(d);
        await migrator.MigrateAsync("20261117130910_AddSpendingRecords");
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        var saved = await context.SpendingRecords.AsNoTracking().Include(r => r.Lines).SingleAsync();
        Assert.Equal(receipt.Id, saved.Id);
        Assert.Equal(receipt.Revision, saved.Revision);
        Assert.Equal(receipt.CreationRequestHash, saved.CreationRequestHash);
        Assert.Equal(12.50m, saved.CountedAmount);
        Assert.Empty(await context.BudgetSnapshots.ToArrayAsync());
    }
}
