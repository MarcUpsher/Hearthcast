using System.Globalization;
using Hearthcast.Data.Budgets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Spending;
using Hearthcast.Data.Validation;

namespace Hearthcast.Data.Tests.Budgets;

public sealed class BudgetTests
{
    private static readonly DateOnly January = new(2026, 1, 1);
    private static readonly DateOnly CheckedOn = new(2025, 12, 28);
    private sealed record Data(Household Home, ExpenseCategory Category, Merchant Merchant, BudgetItem Item)
    {
        internal BudgetEntryMetadata Metadata => BudgetEntryMetadata.FromSetup(Item, Category, Merchant);
    }
    private static Data Setup()
    {
        var home = Household.Create("Home", "Alex");
        var category = new ExpenseCategory(home, "Utilities");
        var merchant = new Merchant(home, "Supplier");
        return new(home, category, merchant, new(category, " energy ", " Energy ", " gbp ", BudgetFrequency.Month,
            merchant: merchant, notes: " Variable usage "));
    }
    private static BudgetSnapshot Plan(Data d, IReadOnlyList<BudgetEntryInput> entries, DateOnly? date = null,
        BudgetSnapshot? source = null) => new(Guid.NewGuid(), d.Home, date ?? January, entries, new string('a', 64), source);
    private static BudgetSnapshot Confirmed(Data d, decimal amount = 200m) => Plan(d, [new(d.Metadata, amount, CheckedOn)]);

    [Fact]
    public void SetupHasNormalisedDefaultsAndNoAmountAndRejectsForeignReferencesAtomically()
    {
        var d = Setup();
        Assert.Equal("ENERGY", d.Item.Code);
        Assert.Equal("Energy", d.Item.Name);
        Assert.Equal("GBP", d.Item.CurrencyCode);
        Assert.Equal("Variable usage", d.Item.Notes);
        Assert.Equal(d.Merchant.Id, d.Item.MerchantId);
        var foreign = Setup();
        Assert.Throws<InvalidValueException>(() => d.Item.SetMerchant(foreign.Merchant));
        Assert.Throws<InvalidValueException>(() => d.Item.SetCategory(foreign.Category));
        Assert.Throws<InvalidValueException>(() => d.Item.SetTerms("USD", (BudgetFrequency)9, 1));
        Assert.Throws<InvalidValueException>(() => d.Item.SetTerms("XXX", BudgetFrequency.Year, 1));
        Assert.Equal("GBP", d.Item.CurrencyCode);
        Assert.Equal(BudgetFrequency.Month, d.Item.Frequency);
        Assert.Equal(d.Category.Id, d.Item.ExpenseCategoryId);
        Assert.Equal(d.Merchant.Id, d.Item.MerchantId);
        d.Item.SetMerchant(null);
        d.Item.SetTerms("USD", BudgetFrequency.Year, 3);
        d.Item.SetRetired(true);
        d.Item.SetNotes("  ");
        Assert.Null(d.Item.MerchantId);
        Assert.Null(d.Item.Notes);
        Assert.Equal(3, d.Item.IntervalCount);
        Assert.True(d.Item.IsRetired);
        Assert.Null(typeof(BudgetItem).GetProperty("PlannedAmount"));
    }

    [Theory]
    [InlineData("", "GBP", 2, 1)]
    [InlineData("bad code", "GBP", 2, 1)]
    [InlineData("-code", "GBP", 2, 1)]
    [InlineData("ENERGY", "ZZZ", 2, 1)]
    [InlineData("ENERGY", "GBP", 0, 1)]
    [InlineData("ENERGY", "GBP", 4, 1)]
    [InlineData("ENERGY", "GBP", 2, 0)]
    [InlineData("ENERGY", "GBP", 2, -1)]
    public void InvalidSetupKeysCurrencyAndRecurrenceAreRejected(string code, string currency, int frequency, int interval)
    {
        var d = Setup();
        Assert.Throws<InvalidValueException>(() => new BudgetItem(d.Category, code, "Energy", currency, (BudgetFrequency)frequency, interval));
    }

    [Fact]
    public void SetupAndHistoricalMetadataUseTheSharedTextAndCodePolicy()
    {
        var d = Setup();
        Assert.Throws<InvalidValueException>(() => new BudgetItem(d.Category, new string('A', 65), "Energy", "GBP", BudgetFrequency.Month));
        d.Item.Rename("\u00a0Energy\u2000");
        Assert.Equal("Energy", d.Item.Name);
        Assert.Throws<InvalidValueException>(() => d.Item.Rename("\u00a0\u2000"));
        Assert.Throws<InvalidValueException>(() => d.Item.SetNotes("bad\0note"));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata with { MerchantName = null })]));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata with { MerchantId = null })]));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata with { HouseholdId = Guid.NewGuid() })]));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata with { BudgetItemId = Guid.Empty })]));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata with { ExpenseCategoryId = Guid.Empty })]));
        Assert.Throws<InvalidValueException>(() => BudgetEntryMetadata.FromSetup(d.Item, d.Category));
    }

    [Fact]
    public void EmptyOmittedUnrecordedAndZeroRemainDistinctAndDifferentCurrenciesStaySeparate()
    {
        var d = Setup();
        var empty = Plan(d, []);
        Assert.Empty(empty.Entries);
        Assert.True(empty.IsComplete);
        var unrecorded = Plan(d, [new(d.Metadata)]);
        Assert.False(unrecorded.IsComplete);
        var entry = Assert.Single(unrecorded.Entries);
        Assert.Null(entry.PlannedAmount);
        Assert.Null(entry.LastConfirmedDate);
        Assert.Null(entry.CarriedFromBudgetEntryId);
        Assert.Equal(BudgetValueOrigin.Unrecorded, entry.ValueOrigin);
        var zero = Confirmed(d, 0);
        Assert.True(zero.IsComplete);
        Assert.Equal(0m, zero.Entries[0].PlannedAmount);
        var food = new BudgetItem(d.Category, "FOOD", "Food", "ZAR", BudgetFrequency.Week, 2);
        var mixed = Plan(d, [new(d.Metadata, 0, CheckedOn), new(BudgetEntryMetadata.FromSetup(food, d.Category), 10.123456m, CheckedOn)]);
        Assert.True(mixed.IsComplete);
        Assert.Equal(new[] { "GBP", "ZAR" }, mixed.Entries.Select(e => e.CurrencyCode));
        Assert.Null(typeof(BudgetSnapshot).GetProperty("TotalAmount"));
    }

    [Theory]
    [InlineData("-0.000001")]
    [InlineData("1000000000000")]
    [InlineData("0.0000001")]
    public void PlannedAmountsRejectBoundsAndExcessPrecisionWithoutRounding(string value)
    {
        var d = Setup();
        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);
        Assert.Throws<InvalidValueException>(() => Confirmed(d, amount));
        var plan = Confirmed(d);
        var revision = plan.Revision;
        Assert.Throws<InvalidValueException>(() => plan.CorrectAmount(revision, plan.Entries[0].Id, amount, CheckedOn, "new"));
        Assert.Equal(200m, plan.Entries[0].PlannedAmount);
        Assert.Equal(revision, plan.Revision);
    }

    [Fact]
    public void ConfirmationDatesAreExplicitIndependentOfTheEffectiveDateAndFinite()
    {
        var d = Setup();
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata, 1)]));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata, ConfirmedOn: CheckedOn)]));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata, 1, DateOnly.MinValue)]));
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata, 1, DateOnly.MaxValue)]));
        var plan = Confirmed(d, 999_999_999_999.999999m);
        Assert.Equal(CheckedOn, plan.Entries[0].LastConfirmedDate);
        Assert.NotEqual(plan.EffectiveFrom, plan.Entries[0].LastConfirmedDate);
    }

    [Fact]
    public void CarryCopiesSavedMetadataAndValuesInsteadOfCurrentSetupAndLaterSourceEdits()
    {
        var d = Setup();
        var source = Confirmed(d);
        d.Item.Rename("New name"); d.Item.SetRetired(true);
        d.Item.SetTerms("USD", BudgetFrequency.Year, 2);
        d.Category.Rename("New category"); d.Merchant.Rename("New supplier");
        var next = Plan(d, [BudgetEntryInput.Carry(source, source.Entries[0])], new(2026, 2, 15), source);
        var entry = Assert.Single(next.Entries);
        Assert.Equal("Energy", entry.ItemName);
        Assert.Equal("Utilities", entry.ExpenseCategoryName);
        Assert.Equal("Supplier", entry.MerchantName);
        Assert.Equal("GBP", entry.CurrencyCode);
        Assert.Equal(BudgetFrequency.Month, entry.Frequency);
        Assert.Equal(1, entry.IntervalCount);
        Assert.Equal(200m, entry.PlannedAmount);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal(source.Entries[0].Id, entry.CarriedFromBudgetEntryId);
        Assert.Equal(source.Id, next.CopiedFromBudgetSnapshotId);
        var revision = next.Revision;
        source.CorrectAmount(source.Revision, source.Entries[0].Id, 20m, new(2026, 2, 1), null);
        Assert.Equal(200m, entry.PlannedAmount);
        Assert.Equal(revision, next.Revision);
        // A compatible source amount correction does not invalidate the independent copy.
        next.CorrectHeader(next.Revision, new(2026, 3, 1), null, [source]);
        Assert.Equal(source.Entries[0].Id, entry.CarriedFromBudgetEntryId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataOnlyCorrectionPreservesLinkedOrDetachedCarryAfterTheSourceAmountChanges(bool detached)
    {
        var d = Setup();
        var source = Confirmed(d);
        var next = Plan(d, [BudgetEntryInput.Carry(source, source.Entries[0])], new(2026, 2, 15), source);
        var entry = next.Entries[0];
        source.CorrectAmount(source.Revision, source.Entries[0].Id, 250, new(2026, 2, 1), "Source correction");
        if (detached) next.DetachSources(next.Revision, [entry.Id]);
        var revision = next.Revision;
        var sourceRevision = source.Revision;
        var category = new ExpenseCategory(d.Home, "Household bills");
        var merchant = new Merchant(d.Home, "Corrected provider");
        next.CorrectEntryMetadata(revision, entry.Id, BudgetEntryMetadata.FromEntry(entry) with
        {
            ItemCode = " ENERGY-OLD ", ItemName = " Historical energy ", ExpenseCategoryId = category.Id,
            ExpenseCategoryName = category.Name, MerchantId = merchant.Id, MerchantName = merchant.Name,
            Notes = " Description correction "
        });
        Assert.Equal(200, entry.PlannedAmount);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal(detached ? (Guid?)null : source.Entries[0].Id, entry.CarriedFromBudgetEntryId);
        Assert.Equal("ENERGY-OLD", entry.ItemCode);
        Assert.Equal("Historical energy", entry.ItemName);
        Assert.Equal(category.Id, entry.ExpenseCategoryId);
        Assert.Equal(merchant.Id, entry.MerchantId);
        Assert.Equal("Description correction", entry.Notes);
        Assert.NotEqual(revision, next.Revision);
        Assert.Equal(sourceRevision, source.Revision);
        Assert.Equal(source.Id, next.CopiedFromBudgetSnapshotId);
        // Explicitly confirming that same number remains a distinct operation.
        next.CorrectAmount(next.Revision, entry.Id, 200, new(2026, 2, 2), entry.Notes);
        Assert.Equal(BudgetValueOrigin.Confirmed, entry.ValueOrigin);
        Assert.Equal(new DateOnly(2026, 2, 2), entry.LastConfirmedDate);
        Assert.Null(entry.CarriedFromBudgetEntryId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataOnlyCorrectionAlsoPreservesUnrecordedAndConfirmedStates(bool confirmed)
    {
        var d = Setup();
        var plan = confirmed ? Confirmed(d, 0) : Plan(d, [new(d.Metadata)]);
        var entry = plan.Entries[0];
        var origin = entry.ValueOrigin;
        var amount = entry.PlannedAmount;
        var date = entry.LastConfirmedDate;
        plan.CorrectEntryMetadata(plan.Revision, entry.Id, BudgetEntryMetadata.FromEntry(entry) with
        { MerchantId = null, MerchantName = null, Notes = null });
        Assert.Equal(amount, entry.PlannedAmount);
        Assert.Equal(date, entry.LastConfirmedDate);
        Assert.Equal(origin, entry.ValueOrigin);
        Assert.Null(entry.CarriedFromBudgetEntryId);
        Assert.Null(entry.MerchantId);
        Assert.Equal(confirmed, plan.IsComplete);
    }

    [Theory]
    [InlineData("item")]
    [InlineData("currency")]
    [InlineData("frequency")]
    [InlineData("interval")]
    public void MetadataOnlyCorrectionRejectsChangesToTheAmountsMeaningBeforeMutating(string change)
    {
        var d = Setup();
        var plan = Confirmed(d);
        var entry = plan.Entries[0];
        var revision = plan.Revision;
        var metadata = BudgetEntryMetadata.FromEntry(entry) with { ItemName = "Should not save" };
        metadata = change switch
        {
            "item" => metadata with { BudgetItemId = Guid.NewGuid() },
            "currency" => metadata with { CurrencyCode = "USD" },
            "frequency" => metadata with { Frequency = BudgetFrequency.Year },
            _ => metadata with { IntervalCount = 2 }
        };
        Assert.Throws<InvalidValueException>(() => plan.CorrectEntryMetadata(revision, entry.Id, metadata));
        Assert.Equal(revision, plan.Revision);
        Assert.Equal(d.Metadata, BudgetEntryMetadata.FromEntry(entry));
        Assert.Equal(200, entry.PlannedAmount);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.Equal(BudgetValueOrigin.Confirmed, entry.ValueOrigin);
        // An explicit value/confirmation allows the deliberate change.
        plan.CorrectEntry(revision, entry.Id, new(metadata, 20, new(2026, 2, 2)));
        Assert.Equal(20, entry.PlannedAmount);
        Assert.Equal(new DateOnly(2026, 2, 2), entry.LastConfirmedDate);
    }

    [Fact]
    public void InvalidMetadataAndStaleMetadataCorrectionsLeaveValuesAndRevisionUnchanged()
    {
        var d = Setup();
        var source = Confirmed(d);
        var next = Plan(d, [BudgetEntryInput.Carry(source, source.Entries[0])], new(2026, 2, 1));
        var entry = next.Entries[0];
        var revision = next.Revision;
        var metadata = BudgetEntryMetadata.FromEntry(entry);
        foreach (var invalid in new[]
        {
            metadata with { Notes = "bad\0notes", ItemName = "Should not save" },
            metadata with { MerchantName = null }, metadata with { HouseholdId = Guid.NewGuid() },
            metadata with { CurrencyCode = "12A" }
        })
            Assert.Throws<InvalidValueException>(() => next.CorrectEntryMetadata(revision, entry.Id, invalid));
        Assert.Throws<InvalidValueException>(() => next.CorrectEntryMetadata(revision, Guid.NewGuid(), metadata));
        Assert.Throws<BudgetSnapshotConflictException>(() => next.CorrectEntryMetadata(Guid.NewGuid(), entry.Id, metadata));
        Assert.Equal(revision, next.Revision);
        Assert.Equal(metadata, BudgetEntryMetadata.FromEntry(entry));
        Assert.Equal(200, entry.PlannedAmount);
        Assert.Equal(CheckedOn, entry.LastConfirmedDate);
        Assert.Equal(source.Entries[0].Id, entry.CarriedFromBudgetEntryId);
        Assert.Equal(BudgetValueOrigin.CarriedForward, entry.ValueOrigin);
    }

    [Fact]
    public void RetiredItemsCanBeIncludedWithDeliberateHistoricalMetadataOrRefreshedDefaults()
    {
        var d = Setup();
        d.Item.SetRetired(true); d.Category.SetRetired(true); d.Merchant.SetRetired(true);
        var plan = Plan(d, [new(d.Metadata with { ItemName = "Historical energy", CurrencyCode = "USD", Frequency = BudgetFrequency.Year }, 25, CheckedOn)]);
        Assert.Equal("Historical energy", plan.Entries[0].ItemName);
        Assert.Equal("USD", plan.Entries[0].CurrencyCode);
        Assert.Equal(BudgetFrequency.Year, plan.Entries[0].Frequency);
        Assert.Equal("GBP", d.Item.CurrencyCode);
        plan.CorrectEntry(plan.Revision, plan.Entries[0].Id, new(d.Metadata, 200, CheckedOn));
        Assert.Equal("Energy", plan.Entries[0].ItemName);
        Assert.Equal("GBP", plan.Entries[0].CurrencyCode);
    }

    [Fact]
    public void UnrecordedCopiesStayUnrecordedAndAnExplicitUnchangedAmountConfirmsACarriedValue()
    {
        var d = Setup();
        var source = Plan(d, [new(d.Metadata)]);
        var next = Plan(d, [BudgetEntryInput.Carry(source, source.Entries[0])], new(2026, 2, 1), source);
        Assert.Equal(BudgetValueOrigin.Unrecorded, next.Entries[0].ValueOrigin);
        Assert.Null(next.Entries[0].CarriedFromBudgetEntryId);
        source.CorrectAmount(source.Revision, source.Entries[0].Id, 200, CheckedOn, null);
        var carried = Plan(d, [BudgetEntryInput.Carry(source, source.Entries[0])], new(2026, 2, 1));
        var entry = carried.Entries[0];
        carried.CorrectAmount(carried.Revision, entry.Id, 200, new(2026, 2, 2), "Checked");
        Assert.Equal(BudgetValueOrigin.Confirmed, entry.ValueOrigin);
        Assert.Equal(new DateOnly(2026, 2, 2), entry.LastConfirmedDate);
        Assert.Null(entry.CarriedFromBudgetEntryId);
        carried.CorrectAmount(carried.Revision, entry.Id, null, null, null);
        Assert.Equal(BudgetValueOrigin.Unrecorded, entry.ValueOrigin);
        Assert.Null(entry.LastConfirmedDate);
        Assert.Null(entry.PlannedAmount);
    }

    [Theory]
    [InlineData("sameDate")]
    [InlineData("laterDate")]
    [InlineData("household")]
    [InlineData("item")]
    [InlineData("currency")]
    [InlineData("frequency")]
    [InlineData("interval")]
    [InlineData("entry")]
    [InlineData("snapshot")]
    [InlineData("amount")]
    [InlineData("confirmation")]
    public void CarryRequiresAnEarlierCompatibleResolvedSourceWithoutValueOverrides(string error)
    {
        var d = Setup();
        var source = Confirmed(d);
        var input = BudgetEntryInput.Carry(source, source.Entries[0]);
        input = error switch
        {
            "item" => input with { Metadata = input.Metadata with { BudgetItemId = Guid.NewGuid() } },
            "currency" => input with { Metadata = input.Metadata with { CurrencyCode = "USD" } },
            "frequency" => input with { Metadata = input.Metadata with { Frequency = BudgetFrequency.Year } },
            "interval" => input with { Metadata = input.Metadata with { IntervalCount = 2 } },
            "entry" => input with { CarrySourceEntryId = Guid.NewGuid() },
            "snapshot" => input with { CarrySourceSnapshot = null },
            "amount" => input with { PlannedAmount = 1 },
            "confirmation" => input with { ConfirmedOn = CheckedOn },
            _ => input
        };
        var date = error == "sameDate" ? January : error == "laterDate" ? January.AddDays(-1) : new DateOnly(2026, 2, 1);
        Assert.Throws<InvalidValueException>(() => Plan(error == "household" ? Setup() : d, [input], date));
    }

    [Fact]
    public void TemplateSourcesAlsoRequireEarlierPlansAndNonemptyCreateIdentities()
    {
        var d = Setup();
        var source = Confirmed(d);
        Assert.Throws<InvalidValueException>(() => Plan(d, [], January, source));
        Assert.Throws<InvalidValueException>(() => Plan(Setup(), [], January.AddDays(1), source));
        Assert.Throws<InvalidValueException>(() => new BudgetSnapshot(source.Id, d.Home, January.AddDays(1), [], new string('a', 64), source));
        Assert.Throws<InvalidValueException>(() => new BudgetSnapshot(Guid.Empty, d.Home, January, [], new string('a', 64)));
        foreach (var hash in new[] { "", "invalid", new string('A', 64), new string('g', 64) })
            Assert.Throws<InvalidValueException>(() => new BudgetSnapshot(Guid.NewGuid(), d.Home, January, [], hash));
        Assert.Throws<InvalidValueException>(() => Plan(d, [], DateOnly.MinValue));
        Assert.Throws<InvalidValueException>(() => Plan(d, [], DateOnly.MaxValue));
    }

    [Fact]
    public void SourceDetachmentPreservesProvenanceAndDateCorrectionsValidateRetainedLinks()
    {
        var d = Setup();
        var source = Confirmed(d);
        var next = Plan(d, [BudgetEntryInput.Carry(source, source.Entries[0])], new(2026, 2, 1), source);
        var revision = next.Revision;
        Assert.Throws<InvalidValueException>(() => next.CorrectHeader(revision, January, "change", [source]));
        Assert.Throws<InvalidValueException>(() => next.CorrectHeader(revision, January.AddDays(1), "change"));
        Assert.Equal(revision, next.Revision);
        Assert.Null(next.Notes);
        next.DetachSources(revision, [next.Entries[0].Id], clearTemplate: true);
        Assert.NotEqual(revision, next.Revision);
        Assert.Null(next.CopiedFromBudgetSnapshotId);
        Assert.Null(next.Entries[0].CarriedFromBudgetEntryId);
        Assert.Equal(BudgetValueOrigin.CarriedForward, next.Entries[0].ValueOrigin);
        Assert.Equal(200m, next.Entries[0].PlannedAmount);
        Assert.Equal(CheckedOn, next.Entries[0].LastConfirmedDate);
        next.CorrectHeader(next.Revision, January.AddDays(-1), "Backdated");
        Assert.Equal(source.CreationRequestHash, next.CreationRequestHash);
        Assert.Equal("Backdated", next.Notes);
    }

    [Fact]
    public void InvalidCorrectionsDuplicateItemsAndStaleEditsLeaveTheAggregateUnchanged()
    {
        var d = Setup();
        var plan = Confirmed(d);
        var entry = plan.Entries[0];
        var revision = plan.Revision;
        Assert.Throws<InvalidValueException>(() => Plan(d, [new(d.Metadata), new(d.Metadata)]));
        Assert.Throws<InvalidValueException>(() => plan.AddEntry(revision, new(d.Metadata)));
        Assert.Throws<InvalidValueException>(() => plan.CorrectEntry(revision, entry.Id, new(d.Metadata with { ItemName = "New" }, -1, CheckedOn)));
        Assert.Throws<InvalidValueException>(() => plan.CorrectAmount(revision, entry.Id, 20, CheckedOn, "bad\0notes"));
        Assert.Throws<InvalidValueException>(() => plan.DetachSources(revision, [entry.Id, Guid.NewGuid()], true));
        Assert.Throws<BudgetSnapshotConflictException>(() => plan.CorrectAmount(Guid.NewGuid(), entry.Id, 1, CheckedOn, null));
        Assert.Throws<BudgetSnapshotConflictException>(() => plan.RemoveEntry(Guid.Empty, entry.Id));
        Assert.Equal(revision, plan.Revision);
        Assert.Equal("Energy", entry.ItemName);
        Assert.Equal(200m, entry.PlannedAmount);
        var removed = plan.RemoveEntry(revision, entry.Id);
        Assert.Same(entry, removed);
        Assert.True(plan.IsComplete);
        Assert.Empty(plan.Entries);
        plan.AddEntry(plan.Revision, new(d.Metadata));
        Assert.NotEqual(entry.Id, plan.Entries[0].Id);
        Assert.False(plan.IsComplete);
    }
}
