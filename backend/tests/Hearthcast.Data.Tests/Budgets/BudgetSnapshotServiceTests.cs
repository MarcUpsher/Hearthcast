using System.Data.Common;
using Hearthcast.Data.Budgets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Hearthcast.Data.Tests.Budgets;

public sealed class BudgetSnapshotServiceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private BudgetSnapshotService Service => new(_database);
    private BudgetSetupService Setup => new(_database);
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly CheckedOn = new(2025, 12, 28);
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();
    private sealed record Data(Guid Home, ExpenseCategory Category, Merchant Merchant, BudgetItem Energy, BudgetItem Food);
    private async Task<Data> SeedAsync()
    {
        var home = await new HouseholdService(_database).CreateAsync("Home", "Alex");
        var spending = new SpendingSetupService(_database);
        var category = await spending.CreateCategoryAsync(home.Id, "Bills");
        var merchant = await spending.CreateMerchantAsync(home.Id, "Supplier");
        var energy = await Setup.CreateItemAsync(home.Id, category.Id, "ENERGY", "Energy", "GBP", BudgetFrequency.Month, merchantId: merchant.Id, notes: "Usage");
        var food = await Setup.CreateItemAsync(home.Id, category.Id, "FOOD", "Food allowance", "ZAR", BudgetFrequency.Week, 2);
        return new(home.Id, category, merchant, energy, food);
    }
    private async Task<BudgetSnapshot> CreateAsync(Data d, DateOnly? date = null, decimal? amount = 200m)
    {
        var mode = amount.HasValue ? BudgetEntryValueMode.Confirm : BudgetEntryValueMode.Unrecorded;
        return (await Service.CreateAsync(d.Home, Guid.NewGuid(), new(date ?? January,
            [new(d.Energy.Id, mode, amount, amount.HasValue ? CheckedOn : null)]))).Snapshot;
    }
    private async Task<BudgetSnapshot> CarryAsync(Data d, BudgetSnapshot source, DateOnly? date = null) =>
        (await Service.CreateAsync(d.Home, Guid.NewGuid(), new(date ?? source.EffectiveFrom.AddMonths(1),
            [new(d.Energy.Id, BudgetEntryValueMode.Carry)], source.Id))).Snapshot;
    private static BudgetEntryMetadataValues Metadata(BudgetEntry e) => new(e.ItemCode, e.ItemName,
        e.ExpenseCategoryId, e.ExpenseCategoryName, e.CurrencyCode, e.Frequency, e.IntervalCount, e.MerchantId, e.MerchantName, e.Notes);

    [Fact]
    public async Task ExplicitPlansDistinguishEmptyOmittedUnrecordedZeroAndDifferentCurrencies()
    {
        var d = await SeedAsync();
        var first = await Service.CreateAsync(d.Home, Guid.NewGuid(), new(January,
            [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 0, CheckedOn), new(d.Food.Id, BudgetEntryValueMode.Unrecorded)], Notes: " Plan "));
        Assert.True(first.WasCreated);
        Assert.False(first.Snapshot.IsComplete);
        Assert.Equal("Plan", first.Snapshot.Notes);
        Assert.Equal(0, first.Snapshot.Entries.Single(e => e.BudgetItemId == d.Energy.Id).PlannedAmount);
        Assert.Null(first.Snapshot.Entries.Single(e => e.BudgetItemId == d.Food.Id).PlannedAmount);
        var empty = await Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 2, 1), [], first.Snapshot.Id));
        Assert.Empty(empty.Snapshot.Entries);
        Assert.True(empty.Snapshot.IsComplete);
        Assert.Equal(first.Snapshot.Id, empty.Snapshot.CopiedFromBudgetSnapshotId);
        var mixed = await Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 3, 1),
            [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 200.123456m, CheckedOn), new(d.Food.Id, BudgetEntryValueMode.Confirm, 500, CheckedOn)]));
        Assert.True(mixed.Snapshot.IsComplete);
        Assert.Equal(new[] { "GBP", "ZAR" }, mixed.Snapshot.Entries.Select(e => e.CurrencyCode).Order());
        Assert.Equal(3, (await Service.ListAsync(d.Home)).Records.Count);
    }

    [Fact]
    public async Task CopyUsesSavedContextWhileExplicitRefreshAndManualHistoricalMetadataAreDeliberate()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var spending = new SpendingSetupService(_database);
        await spending.RenameCategoryAsync(d.Home, d.Category.Id, "New bills");
        await spending.RenameMerchantAsync(d.Home, d.Merchant.Id, "New supplier");
        await Setup.UpdateItemAsync(d.Home, d.Energy.Id, d.Category.Id, "New energy", "USD", BudgetFrequency.Year, 3, d.Merchant.Id, "New notes");
        await Setup.SetItemRetiredAsync(d.Home, d.Energy.Id, true);
        var carried = await CarryAsync(d, source);
        var entry = Assert.Single(carried.Entries);
        Assert.Equal("Energy", entry.ItemName);
        Assert.Equal("Bills", entry.ExpenseCategoryName);
        Assert.Equal("Supplier", entry.MerchantName);
        Assert.Equal("GBP", entry.CurrencyCode);
        Assert.Equal(BudgetFrequency.Month, entry.Frequency);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(),
            new(new(2026, 3, 1), [new(d.Energy.Id, BudgetEntryValueMode.Carry, RefreshFromSetup: true)], source.Id)));
        var refreshed = await Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 3, 1),
            [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 20, new(2026, 2, 20), RefreshFromSetup: true)], source.Id));
        Assert.Equal("New energy", refreshed.Snapshot.Entries[0].ItemName);
        Assert.Equal("USD", refreshed.Snapshot.Entries[0].CurrencyCode);
        Assert.Equal(BudgetFrequency.Year, refreshed.Snapshot.Entries[0].Frequency);
        var historical = Metadata(source.Entries[0]) with { ItemName = "Historical label", MerchantId = null, MerchantName = null };
        var manual = await Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2025, 1, 1),
            [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 50, CheckedOn, Metadata: historical)]));
        Assert.Equal("Historical label", manual.Snapshot.Entries[0].ItemName);
        Assert.Null(manual.Snapshot.Entries[0].MerchantId);
    }

    [Fact]
    public async Task CarryingUnrecordedOrDetachedValuesDoesNotInventConfirmationDates()
    {
        var d = await SeedAsync();
        var unknown = await CreateAsync(d, amount: null);
        var copied = await CarryAsync(d, unknown);
        Assert.Equal(BudgetValueOrigin.Unrecorded, copied.Entries[0].ValueOrigin);
        Assert.Null(copied.Entries[0].CarriedFromBudgetEntryId);
        Assert.Null(copied.Entries[0].LastConfirmedDate);
        copied = await Service.CorrectAmountAsync(d.Home, copied.Id, copied.Revision, copied.Entries[0].Id, 0, CheckedOn, null);
        var third = await CarryAsync(d, copied);
        await Service.RemoveEntryAsync(d.Home, copied.Id, copied.Revision, copied.Entries[0].Id);
        third = await Service.GetAsync(d.Home, third.Id);
        Assert.Equal(BudgetValueOrigin.CarriedForward, third.Entries[0].ValueOrigin);
        Assert.Null(third.Entries[0].CarriedFromBudgetEntryId);
        var fourth = await CarryAsync(d, third);
        Assert.Equal(0, fourth.Entries[0].PlannedAmount);
        Assert.Equal(CheckedOn, fourth.Entries[0].LastConfirmedDate);
        Assert.Equal(third.Entries[0].Id, fourth.Entries[0].CarriedFromBudgetEntryId);
    }

    [Fact]
    public async Task ReplayRetainsIdentityAfterSourceRemovalDateCorrectionMetadataEditsAndEmptyingThePlan()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var id = Guid.NewGuid();
        var values = new BudgetSnapshotValues(new(2026, 2, 1), [new(d.Energy.Id, BudgetEntryValueMode.Carry)], source.Id, " Notes ");
        var first = await Service.CreateAsync(d.Home, id, values);
        var originalHash = first.Snapshot.CreationRequestHash;
        await Service.RemoveEntryAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id);
        var plan = await Service.GetAsync(d.Home, id);
        plan = await Service.CorrectEntryMetadataAsync(d.Home, id, plan.Revision, plan.Entries[0].Id, Metadata(plan.Entries[0]) with { ItemName = "Corrected" });
        plan = await Service.RemoveEntryAsync(d.Home, id, plan.Revision, plan.Entries[0].Id);
        plan = await Service.CorrectHeaderAsync(d.Home, id, plan.Revision, new(2025, 12, 1), "Date corrected");
        await Setup.UpdateItemAsync(d.Home, d.Energy.Id, d.Category.Id, "Changed defaults", "USD", BudgetFrequency.Year, 2, null, null);
        var retry = await Service.CreateAsync(d.Home, id, values with { Notes = "Notes" });
        Assert.False(retry.WasCreated);
        Assert.Equivalent(plan, retry.Snapshot);
        Assert.Equal(originalHash, retry.Snapshot.CreationRequestHash);
        Assert.Equal(first.Snapshot.CreatedAtUtc, retry.Snapshot.CreatedAtUtc);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateAsync(d.Home, id, values with { Notes = "Different" }));
        Assert.Equal(2, (await Service.ListAsync(d.Home)).Records.Count);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("payload")]
    [InlineData("date")]
    public async Task ConcurrentCreatesResolveIdempotencyAndDateConflictsWithoutDuplicatePlans(string mode)
    {
        var d = await SeedAsync();
        var barrier = new ConcurrentSaveBarrier();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, barrier));
        var id = Guid.NewGuid();
        var values = new BudgetSnapshotValues(January, [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 200, CheckedOn)]);
        async Task<(BudgetSnapshotCaptureResult? Result, Exception? Error)> Attempt(Guid key, BudgetSnapshotValues v)
        {
            BudgetSnapshotCaptureResult? result = null;
            var error = await Record.ExceptionAsync(async () => result = await service.CreateAsync(d.Home, key, v));
            return (result, error);
        }
        var outcomes = await Task.WhenAll(Attempt(id, values), Attempt(mode == "date" ? Guid.NewGuid() : id,
            mode == "payload" ? values with { Notes = "Different" } : values));
        if (mode == "same")
        {
            Assert.All(outcomes, o => Assert.Null(o.Error));
            Assert.Single(outcomes, o => o.Result!.WasCreated);
            Assert.Single(outcomes, o => !o.Result!.WasCreated);
            Assert.Equal(outcomes[0].Result!.Snapshot.Revision, outcomes[1].Result!.Snapshot.Revision);
        }
        else
        {
            Assert.Single(outcomes, o => o.Error is null);
            Assert.IsType<BusinessRuleViolationException>(Assert.Single(outcomes, o => o.Error is not null).Error);
        }
        Assert.Single((await Service.ListAsync(d.Home)).Records);
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.BudgetEntries.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureBatchesItemsAndOneSharedSourceForManyEntries(bool explicitSources)
    {
        var d = await SeedAsync();
        for (var i = 0; i < 16; i++)
            await Setup.CreateItemAsync(d.Home, d.Category.Id, $"ITEM-{i:D2}", $"Outgoing {i}", "GBP", BudgetFrequency.Month,
                merchantId: d.Merchant.Id);
        var items = await Setup.ListItemsAsync(d.Home);
        var source = (await Service.CreateAsync(d.Home, Guid.NewGuid(), new(January,
            items.Select((item, i) => new BudgetEntryValues(item.Id, BudgetEntryValueMode.Confirm, 100 + i, CheckedOn)).ToArray()))).Snapshot;
        var entries = source.Entries.Select(e => new BudgetEntryValues(e.BudgetItemId, BudgetEntryValueMode.Carry,
            Source: explicitSources ? new(source.Id, e.Id) : null)).ToArray();
        var capture = new CaptureSql();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, capture));
        var result = await service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddMonths(1), entries,
            explicitSources ? null : source.Id));
        Assert.Equal(18, result.Snapshot.Entries.Count);
        Assert.All(result.Snapshot.Entries, entry =>
        {
            var original = source.Entries.Single(e => e.BudgetItemId == entry.BudgetItemId);
            Assert.Equal(original.PlannedAmount, entry.PlannedAmount);
            Assert.Equal(original.Id, entry.CarriedFromBudgetEntryId);
        });
        // One empty existing-UUID lookup and one actual source graph, independent of entry count.
        Assert.Equal(2, capture.Commands.Count(sql => sql.Contains("JOIN \"BudgetEntries\"")));
        Assert.InRange(capture.Commands.Count(sql => sql.TrimStart().StartsWith("SELECT ", StringComparison.Ordinal)), 1, 9);
    }

    [Fact]
    public async Task CaptureReloadsBatchedSourceStateAfterASerializationRetry()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var retry = new RetryFirstSave();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, retry));
        var operation = service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddMonths(1),
            [new(d.Energy.Id, BudgetEntryValueMode.Carry, Source: new(source.Id, source.Entries[0].Id))]));
        await retry.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await Service.CorrectAmountAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id, 250, January.AddDays(5), "Changed during aborted attempt");
            await Setup.UpdateItemAsync(d.Home, d.Energy.Id, d.Category.Id, "Updated setup", "USD", BudgetFrequency.Year, 2, null, null);
        }
        finally { retry.Resume.TrySetResult(); }
        var result = await operation;
        Assert.True(retry.Saves >= 2);
        var entry = Assert.Single(result.Snapshot.Entries);
        Assert.Equal(250, entry.PlannedAmount);
        Assert.Equal(January.AddDays(5), entry.LastConfirmedDate);
        Assert.Equal("Energy", entry.ItemName);
        Assert.Equal("GBP", entry.CurrencyCode);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal(source.Entries[0].Id, entry.CarriedFromBudgetEntryId);
    }

    [Fact]
    public async Task SourceAmountAndMetadataCorrectionsPreserveIndependentDescendantsAndExplicitConfirmationIsSeparate()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var child = await CarryAsync(d, source);
        source = await Service.CorrectAmountAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id, 250, new(2026, 1, 5), "Source changed");
        source = await Service.CorrectEntryMetadataAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id,
            Metadata(source.Entries[0]) with { ItemName = "New source description" });
        var unchanged = await Service.GetAsync(d.Home, child.Id);
        Assert.Equivalent(child, unchanged);
        child = await Service.CorrectEntryMetadataAsync(d.Home, child.Id, child.Revision, child.Entries[0].Id,
            Metadata(child.Entries[0]) with { ItemName = "Child description", MerchantId = null, MerchantName = null });
        Assert.Equal(200, child.Entries[0].PlannedAmount);
        Assert.Equal(CheckedOn, child.Entries[0].LastConfirmedDate);
        Assert.Equal(BudgetValueOrigin.CarriedForward, child.Entries[0].ValueOrigin);
        Assert.Equal(source.Entries[0].Id, child.Entries[0].CarriedFromBudgetEntryId);
        child = await Service.CorrectAmountAsync(d.Home, child.Id, child.Revision, child.Entries[0].Id, 200, new(2026, 2, 5), null);
        Assert.Equal(BudgetValueOrigin.Confirmed, child.Entries[0].ValueOrigin);
        Assert.Null(child.Entries[0].CarriedFromBudgetEntryId);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("currency")]
    [InlineData("frequency")]
    [InlineData("interval")]
    public async Task MeaningChangesDetachOnlyDirectIncompatibleCarryLinksAndPreserveAllSavedValues(string field)
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var child = await CarryAsync(d, source);
        var grandchild = await CarryAsync(d, child);
        var metadata = Metadata(source.Entries[0]);
        metadata = field switch
        {
            "currency" => metadata with { CurrencyCode = "USD" },
            "frequency" => metadata with { Frequency = BudgetFrequency.Year },
            "interval" => metadata with { IntervalCount = 3 },
            _ => metadata
        };
        var changed = await Service.CorrectEntryAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id,
            new(field == "item" ? d.Food.Id : d.Energy.Id, BudgetEntryValueMode.Confirm, 50, CheckedOn, Metadata: metadata));
        Assert.Equal(source.Entries[0].Id, changed.Entries[0].Id);
        var saved = await Service.GetAsync(d.Home, child.Id);
        Assert.Null(saved.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(BudgetValueOrigin.CarriedForward, saved.Entries[0].ValueOrigin);
        Assert.Equal(200, saved.Entries[0].PlannedAmount);
        Assert.Equal(CheckedOn, saved.Entries[0].LastConfirmedDate);
        Assert.Equal("GBP", saved.Entries[0].CurrencyCode);
        Assert.Equal(BudgetFrequency.Month, saved.Entries[0].Frequency);
        Assert.NotEqual(child.Revision, saved.Revision);
        Assert.Equal(source.Id, saved.CopiedFromBudgetSnapshotId);
        Assert.Equivalent(grandchild, await Service.GetAsync(d.Home, grandchild.Id));
    }

    [Fact]
    public async Task AddRemoveAndExplicitRecopyKeepCreationIdentityAndEnforceUniqueItems()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var otherSource = await CreateAsync(d, January.AddDays(10), 100);
        var child = await CarryAsync(d, source);
        var hash = child.CreationRequestHash;
        child = await Service.AddEntryAsync(d.Home, child.Id, child.Revision, new(d.Food.Id, BudgetEntryValueMode.Unrecorded));
        Assert.Equal(2, child.Entries.Count);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.AddEntryAsync(d.Home, child.Id, child.Revision, new(d.Food.Id, BudgetEntryValueMode.Unrecorded)));
        var energy = child.Entries.Single(e => e.BudgetItemId == d.Energy.Id);
        child = await Service.CorrectEntryAsync(d.Home, child.Id, child.Revision, energy.Id,
            new(d.Energy.Id, BudgetEntryValueMode.Carry, Source: new(otherSource.Id, otherSource.Entries[0].Id)));
        Assert.Equal(100, child.Entries.Single(e => e.Id == energy.Id).PlannedAmount);
        Assert.Equal(source.Id, child.CopiedFromBudgetSnapshotId);
        child = await Service.RemoveEntryAsync(d.Home, child.Id, child.Revision, energy.Id);
        Assert.Single(child.Entries);
        Assert.Equal(hash, child.CreationRequestHash);
        Assert.Equal(3, (await Service.ListAsync(d.Home)).Records.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DateCorrectionsDetachInvalidOutgoingOrIncomingLinksAndPreserveCompatibleLinks(bool moveAfterChild)
    {
        var d = await SeedAsync();
        var first = await CreateAsync(d);
        var middle = await CarryAsync(d, first);
        var last = await CarryAsync(d, middle);
        var date = moveAfterChild ? new DateOnly(2026, 4, 1) : new DateOnly(2025, 12, 1);
        var corrected = await Service.CorrectHeaderAsync(d.Home, middle.Id, middle.Revision, date, "Date correction");
        var child = await Service.GetAsync(d.Home, last.Id);
        Assert.Equal(date, corrected.EffectiveFrom);
        Assert.Equal(200, corrected.Entries[0].PlannedAmount);
        Assert.Equal(BudgetValueOrigin.CarriedForward, corrected.Entries[0].ValueOrigin);
        if (moveAfterChild)
        {
            Assert.Equal(first.Id, corrected.CopiedFromBudgetSnapshotId);
            Assert.Equal(first.Entries[0].Id, corrected.Entries[0].CarriedFromBudgetEntryId);
            Assert.Null(child.CopiedFromBudgetSnapshotId);
            Assert.Null(child.Entries[0].CarriedFromBudgetEntryId);
            Assert.NotEqual(last.Revision, child.Revision);
        }
        else
        {
            Assert.Null(corrected.CopiedFromBudgetSnapshotId);
            Assert.Null(corrected.Entries[0].CarriedFromBudgetEntryId);
            Assert.Equivalent(last, child);
        }
        Assert.Equal(last.CreationRequestHash, child.CreationRequestHash);
        Assert.Equal(200, child.Entries[0].PlannedAmount);
        Assert.Equal(CheckedOn, child.Entries[0].LastConfirmedDate);
    }

    [Fact]
    public async Task SourceRemovalDetachesBeforeDeleteAndAFailedDeleteRollsBackEveryRevisionAndLink()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var child = await CarryAsync(d, source);
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION "RejectBudgetEntryDelete"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Synthetic delete failure'; END; $$;
                CREATE TRIGGER "RejectBudgetEntryDelete" BEFORE DELETE ON "BudgetEntries"
                FOR EACH ROW EXECUTE FUNCTION "RejectBudgetEntryDelete"();
                """);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service.RemoveEntryAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id));
        Assert.Equivalent(source, await Service.GetAsync(d.Home, source.Id));
        Assert.Equivalent(child, await Service.GetAsync(d.Home, child.Id));
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlRawAsync("""DROP TRIGGER "RejectBudgetEntryDelete" ON "BudgetEntries";""");
        var empty = await Service.RemoveEntryAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id);
        Assert.Empty(empty.Entries);
        var detached = await Service.GetAsync(d.Home, child.Id);
        Assert.Null(detached.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(BudgetValueOrigin.CarriedForward, detached.Entries[0].ValueOrigin);
        Assert.Equal(200, detached.Entries[0].PlannedAmount);
    }

    [Fact]
    public async Task ConcurrentDescendantEditCausesConflictEvenIfItRemovedTheCarryLinkBeforeRetry()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var child = await CarryAsync(d, source);
        var pause = new PauseFirstSave();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, pause));
        var operation = service.RemoveEntryAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id);
        await pause.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await Service.CorrectAmountAsync(d.Home, child.Id, child.Revision, child.Entries[0].Id, 175, new(2026, 2, 2), "Concurrent confirmation");
        }
        finally { pause.Resume.TrySetResult(); }
        await Assert.ThrowsAsync<BudgetSnapshotConflictException>(() => operation);
        Assert.Equivalent(source, await Service.GetAsync(d.Home, source.Id));
        var saved = await Service.GetAsync(d.Home, child.Id);
        Assert.Equal(175, saved.Entries[0].PlannedAmount);
        Assert.Equal(BudgetValueOrigin.Confirmed, saved.Entries[0].ValueOrigin);
        Assert.Null(saved.Entries[0].CarriedFromBudgetEntryId);
    }

    [Fact]
    public async Task ConcurrentHeaderAndAmountCorrectionsShareOneParentRevision()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var barrier = new ConcurrentSaveBarrier();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, barrier));
        var results = await Task.WhenAll(
            Record.ExceptionAsync(() => service.CorrectHeaderAsync(d.Home, source.Id, source.Revision, January, "Header edit")),
            Record.ExceptionAsync(() => service.CorrectAmountAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id, 20, CheckedOn, "Amount edit")));
        Assert.Single(results, e => e is null);
        Assert.IsType<BudgetSnapshotConflictException>(Assert.Single(results, e => e is not null));
        var saved = await Service.GetAsync(d.Home, source.Id);
        Assert.True((saved.Notes == "Header edit" && saved.Entries[0].PlannedAmount == 200)
            || (saved.Notes is null && saved.Entries[0].PlannedAmount == 20));
    }

    [Fact]
    public async Task ConcurrentCopyAndSourceRemovalCannotLeaveADanglingOrUnlabelledCarry()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var barrier = new ConcurrentSaveBarrier();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, barrier));
        BudgetSnapshotCaptureResult? captured = null;
        var copying = Record.ExceptionAsync(async () => captured = await service.CreateAsync(d.Home, Guid.NewGuid(),
            new(January.AddMonths(1), [new(d.Energy.Id, BudgetEntryValueMode.Carry,
                Source: new(source.Id, source.Entries[0].Id))], source.Id)));
        var removing = Record.ExceptionAsync(() => service.RemoveEntryAsync(d.Home, source.Id, source.Revision, source.Entries[0].Id));
        var results = await Task.WhenAll(copying, removing);
        Assert.Null(results[1]);
        Assert.Empty((await Service.GetAsync(d.Home, source.Id)).Entries);
        if (results[0] is not null) Assert.IsType<ResourceNotFoundException>(results[0]);
        else
        {
            var child = await Service.GetAsync(d.Home, captured!.Snapshot.Id);
            Assert.Equal(200, child.Entries[0].PlannedAmount);
            Assert.Equal(BudgetValueOrigin.CarriedForward, child.Entries[0].ValueOrigin);
            Assert.Null(child.Entries[0].CarriedFromBudgetEntryId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiQueryAsOfReadsKeepOneDatabaseViewWhenAPlanIsInsertedBetweenQueries(bool outgoings)
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var future = await CreateAsync(d, January.AddMonths(2));
        var pause = new PausePlanRead();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, pause));
        Task<BudgetCurrentOutgoings>? outgoingRead = outgoings ? service.GetCurrentOutgoingsAsync(d.Home, January.AddDays(20)) : null;
        Task<BudgetEffectivePlan?>? effectiveRead = outgoings ? null : service.GetEffectiveAsync(d.Home, January.AddDays(20));
        await pause.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        BudgetSnapshot inserted;
        try
        {
            inserted = await CreateAsync(d, January.AddDays(10), 400);
            await Setup.CreateItemAsync(d.Home, d.Category.Id, "NEW", "New outgoing", "GBP", BudgetFrequency.Month);
        }
        finally { pause.Resume.TrySetResult(); }
        if (outgoings)
        {
            var read = await outgoingRead!;
            Assert.Equal(source.Id, read.SnapshotId);
            Assert.Equal(future.EffectiveFrom, read.EffectiveUntilExclusive);
            Assert.Equal(2, read.Items.Count);
            Assert.Equal(200, read.Items.Single(i => i.Item.Id == d.Energy.Id).Entry!.PlannedAmount);
        }
        else
        {
            var read = await effectiveRead!;
            Assert.Equal(source.Id, read!.Snapshot.Id);
            Assert.Equal(future.EffectiveFrom, read.EffectiveUntilExclusive);
        }
        Assert.Equal(inserted.Id, (await Service.GetEffectiveAsync(d.Home, January.AddDays(20)))!.Snapshot.Id);
    }

    [Fact]
    public async Task StaleAndInvalidEditsCannotMutateThePlanOrItsDescendants()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        var child = await CarryAsync(d, source);
        var id = source.Entries[0].Id;
        await Assert.ThrowsAsync<BudgetSnapshotConflictException>(() => Service.CorrectAmountAsync(d.Home, source.Id, Guid.NewGuid(), id, 1, CheckedOn, null));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CorrectAmountAsync(d.Home, source.Id, Guid.Empty, id, 1, CheckedOn, null));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CorrectAmountAsync(d.Home, source.Id, source.Revision, id, 1.0000001m, CheckedOn, null));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CorrectEntryMetadataAsync(d.Home, source.Id, source.Revision, id,
            Metadata(source.Entries[0]) with { CurrencyCode = "USD" }));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CorrectHeaderAsync(d.Home, child.Id, child.Revision, new(2025, 1, 1), "bad\0notes"));
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CorrectHeaderAsync(d.Home, child.Id, child.Revision, January, null));
        Assert.Equivalent(source, await Service.GetAsync(d.Home, source.Id));
        Assert.Equivalent(child, await Service.GetAsync(d.Home, child.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndForeignReferencesAreScopedForCreatesCorrectionsAndReads(bool foreign)
    {
        var d = await SeedAsync();
        var other = await SeedAsync();
        var own = await CreateAsync(d);
        var outside = await CreateAsync(other);
        var planId = foreign ? outside.Id : Guid.NewGuid();
        var itemId = foreign ? other.Energy.Id : Guid.NewGuid();
        var categoryId = foreign ? other.Category.Id : Guid.NewGuid();
        var merchantId = foreign ? other.Merchant.Id : Guid.NewGuid();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetAsync(d.Home, planId));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CorrectHeaderAsync(d.Home, planId, own.Revision, January, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CorrectAmountAsync(d.Home, own.Id, own.Revision, outside.Entries[0].Id, 1, CheckedOn, null));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.RemoveEntryAsync(d.Home, own.Id, own.Revision, outside.Entries[0].Id));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.AddEntryAsync(d.Home, own.Id, own.Revision, new(itemId, BudgetEntryValueMode.Unrecorded)));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 2, 1), [], planId)));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 2, 1),
            [new(d.Energy.Id, BudgetEntryValueMode.Carry, Source: new(planId, outside.Entries[0].Id))])));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CorrectEntryMetadataAsync(d.Home, own.Id, own.Revision, own.Entries[0].Id,
            Metadata(own.Entries[0]) with { ExpenseCategoryId = categoryId }));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CorrectEntryMetadataAsync(d.Home, own.Id, own.Revision, own.Entries[0].Id,
            Metadata(own.Entries[0]) with { MerchantId = merchantId }));
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateAsync(d.Home, outside.Id, new(new(2026, 2, 1), [])));
        Assert.Equivalent(own, await Service.GetAsync(d.Home, own.Id));
        Assert.Equivalent(outside, await Service.GetAsync(other.Home, outside.Id));
    }

    [Fact]
    public async Task CopySourcesMustBeEarlierAndCarryMustUseTheSameItemAndCompatibleTerms()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddDays(-1), [], source.Id)));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddDays(1),
            [new(d.Energy.Id, BudgetEntryValueMode.Carry)])));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddDays(1),
            [new(d.Food.Id, BudgetEntryValueMode.Carry, Source: new(source.Id, source.Entries[0].Id))])));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddDays(1),
            [new(d.Energy.Id, BudgetEntryValueMode.Carry, Source: new(source.Id, Guid.NewGuid()))])));
        Assert.Single((await Service.ListAsync(d.Home)).Records);
    }

    [Fact]
    public async Task EffectiveReadsReturnSavedIntervalsAndCurrentOutgoingsWithoutFallingBackToOlderOmittedEntries()
    {
        var d = await SeedAsync();
        Assert.Null(await Service.GetEffectiveAsync(d.Home, January));
        var unknown = await Service.GetCurrentOutgoingsAsync(d.Home, January);
        Assert.Null(unknown.SnapshotId);
        Assert.Null(unknown.IsComplete);
        Assert.All(unknown.Items, i => Assert.Null(i.Entry));
        var source = await CreateAsync(d);
        var empty = (await Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 2, 15), []))).Snapshot;
        var future = await CreateAsync(d, new(2027, 1, 1), 0);
        var current = await Service.GetEffectiveAsync(d.Home, new(2026, 2, 14));
        Assert.Equal(source.Id, current!.Snapshot.Id);
        Assert.Equal(empty.EffectiveFrom, current.EffectiveUntilExclusive);
        Assert.Equal(source.Id, (await Service.FindCopySourceAsync(d.Home, empty.EffectiveFrom))!.Id);
        Assert.Null(await Service.FindCopySourceAsync(d.Home, January));
        await Setup.UpdateItemAsync(d.Home, d.Energy.Id, d.Category.Id, "Renamed", "USD", BudgetFrequency.Year, 2, null, null);
        await Setup.SetItemRetiredAsync(d.Home, d.Energy.Id, true);
        var read = await Service.GetCurrentOutgoingsAsync(d.Home, January);
        var item = read.Items.Single(i => i.Item.Id == d.Energy.Id);
        Assert.Equal("USD", item.Item.CurrencyCode);
        Assert.Equal("GBP", item.Entry!.CurrencyCode);
        Assert.Equal(200, item.Entry.PlannedAmount);
        Assert.Equal(source.Id, read.SnapshotId);
        Assert.Single((await Service.GetCurrentOutgoingsAsync(d.Home, January, false)).Items);
        var omitted = await Service.GetCurrentOutgoingsAsync(d.Home, new(2026, 2, 15));
        Assert.Equal(empty.Id, omitted.SnapshotId);
        Assert.True(omitted.IsComplete);
        Assert.Equal(future.EffectiveFrom, omitted.EffectiveUntilExclusive);
        Assert.All(omitted.Items, i => Assert.Null(i.Entry));
        var last = await Service.GetEffectiveAsync(d.Home, future.EffectiveFrom);
        Assert.Equal(future.Id, last!.Snapshot.Id);
        Assert.Null(last.EffectiveUntilExclusive);
    }

    [Fact]
    public async Task HistoryPagesLimitCompletePlansAndHaveExclusiveDateContinuation()
    {
        var d = await SeedAsync();
        for (var month = 0; month < 5; month++)
            await Service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddMonths(month),
                [new(d.Energy.Id, BudgetEntryValueMode.Confirm, month, CheckedOn), new(d.Food.Id, BudgetEntryValueMode.Unrecorded)]));
        var page = await Service.ListAsync(d.Home, pageSize: 2);
        Assert.Equal(new[] { January.AddMonths(4), January.AddMonths(3) }, page.Records.Select(s => s.EffectiveFrom));
        Assert.All(page.Records, p => Assert.Equal(2, p.Entries.Count));
        var second = await Service.ListAsync(d.Home, page.NextBefore, 2);
        var last = await Service.ListAsync(d.Home, second.NextBefore, 2);
        Assert.Single(last.Records);
        Assert.Null(last.NextBefore);
        Assert.Equal(5, page.Records.Concat(second.Records).Concat(last.Records).Select(s => s.Id).Distinct().Count());
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.ListAsync(d.Home, pageSize: 0));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.ListAsync(d.Home, pageSize: 101));
    }

    [Fact]
    public async Task HistorySqlLimitsParentsBeforeJoiningEntriesAndCurrentAmountsUseABoundedNumberOfQueries()
    {
        var d = await SeedAsync();
        await CreateAsync(d);
        await Service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddMonths(1),
            [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 200, CheckedOn), new(d.Food.Id, BudgetEntryValueMode.Unrecorded)]));
        var capture = new CaptureSql();
        var service = new BudgetSnapshotService(new InterceptingContextFactory(_database, capture));
        var page = await service.ListAsync(d.Home, pageSize: 1);
        Assert.Equal(2, Assert.Single(page.Records).Entries.Count);
        var sql = Assert.Single(capture.Commands, s => s.Contains("JOIN \"BudgetEntries\""));
        Assert.True(sql.IndexOf("LIMIT", StringComparison.Ordinal) < sql.IndexOf("JOIN \"BudgetEntries\"", StringComparison.Ordinal));
        Assert.DoesNotContain("OFFSET", sql);
        capture.Commands.Clear();
        var amounts = await service.GetCurrentOutgoingsAsync(d.Home, January);
        Assert.Equal(2, amounts.Items.Count);
        Assert.InRange(capture.Commands.Count, 1, 4);
    }

    [Fact]
    public async Task HistoricalCurrenciesRemainUsableForCarryExplicitReconfirmationAndMetadataCorrections()
    {
        var d = await SeedAsync();
        var source = await CreateAsync(d);
        await using (var context = _database.CreateDbContext())
            await context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "BudgetEntries" SET "CurrencyCode" = {"ZZZ"} WHERE "BudgetSnapshotId" = {source.Id}""");
        source = await Service.GetAsync(d.Home, source.Id);
        var carried = await CarryAsync(d, source);
        Assert.Equal("ZZZ", carried.Entries[0].CurrencyCode);
        var confirmed = await Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 3, 1),
            [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 210, CheckedOn)], source.Id));
        Assert.Equal("ZZZ", confirmed.Snapshot.Entries[0].CurrencyCode);
        var edited = await Service.CorrectEntryMetadataAsync(d.Home, carried.Id, carried.Revision, carried.Entries[0].Id,
            Metadata(carried.Entries[0]) with { ItemName = "Historical" });
        Assert.Equal("ZZZ", edited.Entries[0].CurrencyCode);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(new(2026, 4, 1),
            [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 100, CheckedOn, Metadata: Metadata(source.Entries[0]))])));
    }

    [Fact]
    public async Task CallerCollectionsAreFrozenBeforeTheFirstAwaitAndCancellationDoesNotWrite()
    {
        var d = await SeedAsync();
        var factory = new PauseFactory(_database);
        var service = new BudgetSnapshotService(factory);
        BudgetEntryValues[] entries = [new(d.Energy.Id, BudgetEntryValueMode.Confirm, 200, CheckedOn)];
        var operation = service.CreateAsync(d.Home, Guid.NewGuid(), new(January, entries));
        await factory.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
        entries[0] = new(d.Food.Id, BudgetEntryValueMode.Unrecorded);
        factory.Resume.TrySetResult();
        var created = await operation;
        Assert.Equal(d.Energy.Id, created.Snapshot.Entries[0].BudgetItemId);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.CreateAsync(d.Home, Guid.NewGuid(), new(January.AddMonths(1), []), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service.RemoveEntryAsync(d.Home, created.Snapshot.Id,
            created.Snapshot.Revision, created.Snapshot.Entries[0].Id, cancellation.Token));
        Assert.Single((await Service.ListAsync(d.Home)).Records);
    }

    [Fact]
    public async Task MissingHouseholdsAndInvalidReadDatesRemainDistinctFromKnownEmptyInventories()
    {
        var home = await new HouseholdService(_database).CreateAsync("Empty", "Alex");
        Assert.Empty((await Service.ListAsync(home.Id)).Records);
        Assert.Null(await Service.GetEffectiveAsync(home.Id, January));
        var missing = Guid.NewGuid();
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ListAsync(missing));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetEffectiveAsync(missing, January));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.FindCopySourceAsync(missing, January));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetCurrentOutgoingsAsync(missing, January));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(missing, Guid.NewGuid(), new(January, [])));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.GetEffectiveAsync(home.Id, DateOnly.MaxValue));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.GetCurrentOutgoingsAsync(home.Id, DateOnly.MinValue));
    }

    private sealed class PauseFirstSave : SaveChangesInterceptor
    {
        private int _count;
        internal readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _count) == 1)
            {
                Arrived.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
    private sealed class RetryFirstSave : SaveChangesInterceptor
    {
        private int _saves;
        internal int Saves => Volatile.Read(ref _saves);
        internal readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saves) == 1)
            {
                Arrived.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                throw new PostgresException("Synthetic aborted attempt", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure);
            }
            return result;
        }
    }
    private sealed class PausePlanRead : DbCommandInterceptor
    {
        private int _count;
        internal readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"BudgetSnapshots\"") && Interlocked.Increment(ref _count) == 1)
            {
                Arrived.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
    private sealed class CaptureSql : DbCommandInterceptor
    {
        internal readonly List<string> Commands = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
    private sealed class PauseFactory(PostgresTestDatabase inner) : IDbContextFactory<HearthcastDbContext>
    {
        internal readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HearthcastDbContext CreateDbContext() => inner.CreateDbContext();
        public async Task<HearthcastDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            Arrived.TrySetResult();
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return inner.CreateDbContext();
        }
    }
}
