using System.Globalization;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class SnapshotCarryForwardTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private readonly SnapshotService _snapshots;
    private readonly TrackedItemService _items;

    public SnapshotCarryForwardTests()
    {
        _snapshots = new SnapshotService(_database);
        _items = new TrackedItemService(_database);
    }

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("123.45")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001")]
    public async Task SeveralGenerationsPreserveExactAmountsAndOriginalConfirmation(string? amountText)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        decimal? amount = amountText is null ? null : decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var first = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 10), [new(item.Id, amount)]);
        var second = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 3, 1));
        var third = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2030, 9, 1));
        var saved = Assert.Single((await _snapshots.GetAsync(setup.HouseholdId, third.Id)).ItemSnapshots);
        Assert.Equal(amount, saved.Amount);
        Assert.Equal(amount.HasValue ? SnapshotValueOrigin.CarriedForward : SnapshotValueOrigin.Unrecorded, saved.ValueOrigin);
        Assert.Equal(amount.HasValue ? new DateOnly(2023, 1, 10) : null, saved.LastConfirmedDate);
        Assert.Equal(amount.HasValue ? DatePrecision.Day : null, saved.LastConfirmedPrecision);
        Assert.Equal(amount.HasValue ? second.ItemSnapshots.Single().Id : (Guid?)null, saved.CarriedFromItemSnapshotId);
        Assert.Equal(amount.HasValue ? first.ItemSnapshots.Single().Id : (Guid?)null, second.ItemSnapshots.Single().CarriedFromItemSnapshotId);
        await using var context = _database.CreateDbContext();
        Assert.Equal(3, await context.Snapshots.CountAsync());
    }

    [Theory]
    [InlineData("2023-01-01", DatePrecision.Month, "2023-02-01", DatePrecision.Day, "carry")]
    [InlineData("2023-01-31", DatePrecision.Day, "2023-02-01", DatePrecision.Month, "carry")]
    [InlineData("2023-02-01", DatePrecision.Day, "2023-01-01", DatePrecision.Month, "missing")]
    [InlineData("2023-01-01", DatePrecision.Month, "2023-01-18", DatePrecision.Day, "ambiguous")]
    [InlineData("2023-01-18", DatePrecision.Day, "2023-01-01", DatePrecision.Month, "ambiguous")]
    [InlineData("2023-01-18", DatePrecision.Day, "2023-01-18", DatePrecision.Day, "ambiguous")]
    [InlineData("2023-01-01", DatePrecision.Month, "2023-01-01", DatePrecision.Month, "ambiguous")]
    [InlineData("2024-02-29", DatePrecision.Day, "2024-03-01", DatePrecision.Month, "carry")]
    [InlineData("2024-02-01", DatePrecision.Month, "2024-02-29", DatePrecision.Day, "ambiguous")]
    [InlineData("0001-01-01", DatePrecision.Month, "0001-02-01", DatePrecision.Day, "carry")]
    [InlineData("9999-12-31", DatePrecision.Day, "9999-12-01", DatePrecision.Month, "ambiguous")]
    public async Task AutomaticSelectionHonoursDatePrecisionAndCalendarBoundaries(
        string sourceDate, DatePrecision sourcePrecision, string targetDate, DatePrecision targetPrecision, string outcome)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        await _snapshots.CreateAsync(setup.HouseholdId, DateOnly.Parse(sourceDate, CultureInfo.InvariantCulture),
            [new(item.Id, 100m)], sourcePrecision);
        var date = DateOnly.Parse(targetDate, CultureInfo.InvariantCulture);
        if (outcome == "ambiguous")
        {
            await Assert.ThrowsAsync<Hearthcast.Data.Validation.BusinessRuleViolationException>(() => _snapshots.CreateNextAsync(setup.HouseholdId, date, datePrecision: targetPrecision));
            await using var context = _database.CreateDbContext();
            Assert.Equal(1, await context.Snapshots.CountAsync());
        }
        else
        {
            var created = await _snapshots.CreateNextAsync(setup.HouseholdId, date, datePrecision: targetPrecision);
            Assert.Equal(outcome == "carry" ? 100m : (decimal?)null, Assert.Single(created.ItemSnapshots).Amount);
        }
    }

    [Fact]
    public async Task ExplicitSelectionResolvesTiesAndOverlappingPeriodsWithoutUsingEntryTime()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        var dated = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 18), [new(item.Id, 20m)]);
        var month = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)], DatePrecision.Month);
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.BusinessRuleViolationException>(() => _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1)));
        var selected = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 1, 18), sourceSnapshotId: month.Id);
        Assert.Equal(10m, Assert.Single(selected.ItemSnapshots).Amount);
        Assert.Equal(Assert.Single(month.ItemSnapshots).Id, Assert.Single(selected.ItemSnapshots).CarriedFromItemSnapshotId);
        var later = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 2, 5), [new(item.Id, 30m)]);
        var next = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 3, 1));
        Assert.Equal(Assert.Single(later.ItemSnapshots).Id, Assert.Single(next.ItemSnapshots).CarriedFromItemSnapshotId);
        Assert.NotEqual(dated.Id, selected.Id);
    }

    [Fact]
    public async Task SameDayTiesRequireAnExplicitChoiceEvenForAMuchLaterTarget()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        var first = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)]);
        await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 20m)]);
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.BusinessRuleViolationException>(() => _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2024, 1, 1)));
        var selected = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2024, 1, 1), sourceSnapshotId: first.Id);
        Assert.Equal(10m, Assert.Single(selected.ItemSnapshots).Amount);
    }

    [Fact]
    public async Task BackdatedCaptureUsesAnEarlierSourceAndCurrentSetupWithoutBorrowingFutureValues()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        var past = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)]);
        var future = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 3, 1), [new(item.Id, 30m)]);
        var added = await setup.CreateItemAsync(_items);
        var backdated = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        Assert.Equal(past.ItemSnapshots.Single().Id, backdated.ItemSnapshots.Single(entry => entry.TrackedItemId == item.Id).CarriedFromItemSnapshotId);
        Assert.Null(backdated.ItemSnapshots.Single(entry => entry.TrackedItemId == added.Id).Amount);
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.InvalidValueException>(() => _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1), sourceSnapshotId: future.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownAndForeignSourcesCannotCreateACapture(bool foreign)
    {
        var first = await TrackedItemTestSetup.CreateAsync(_database);
        var second = await TrackedItemTestSetup.CreateAsync(_database);
        var source = await _snapshots.CreateAsync(second.HouseholdId, new DateOnly(2023, 1, 1));
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => _snapshots.CreateNextAsync(first.HouseholdId,
            new DateOnly(2023, 2, 1), sourceSnapshotId: foreign ? source.Id : Guid.NewGuid()));
        await using var context = _database.CreateDbContext();
        Assert.Equal(1, await context.Snapshots.CountAsync());
    }

    [Fact]
    public async Task ExplicitlyClearedValuesStayMissingAndUnchangedSuppliedValuesAreReconfirmed()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var clearedItem = await setup.CreateItemAsync(_items);
        var unchangedItem = await setup.CreateItemAsync(_items);
        await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(clearedItem.Id, 50m), new(unchangedItem.Id, 0m)]);
        var next = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1), [new(clearedItem.Id, null), new(unchangedItem.Id, 0m)]);
        var confirmed = next.ItemSnapshots.Single(entry => entry.TrackedItemId == unchangedItem.Id);
        Assert.Equal(SnapshotValueOrigin.Confirmed, confirmed.ValueOrigin);
        Assert.Equal(new DateOnly(2023, 2, 1), confirmed.LastConfirmedDate);
        Assert.Null(confirmed.CarriedFromItemSnapshotId);
        var after = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 3, 1));
        var missing = after.ItemSnapshots.Single(entry => entry.TrackedItemId == clearedItem.Id);
        Assert.Null(missing.Amount);
        Assert.Null(missing.CarriedFromItemSnapshotId);
        Assert.Null(missing.LastConfirmedDate);
        Assert.Equal(SnapshotValueOrigin.Unrecorded, missing.ValueOrigin);
    }

    [Fact]
    public async Task MissingEntriesAndReplacementItemsDoNotFindOlderOrSameNamedValues()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var oldItem = await setup.CreateItemAsync(_items);
        await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(oldItem.Id, 100m)]);
        await _items.SetRetiredAsync(setup.HouseholdId, oldItem.Id, true);
        var replacement = await setup.CreateItemAsync(_items); // Deliberately the exact same display name.
        var gap = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        Assert.Equal(replacement.Id, Assert.Single(gap.ItemSnapshots).TrackedItemId);
        Assert.Null(Assert.Single(gap.ItemSnapshots).Amount);
        await _items.SetRetiredAsync(setup.HouseholdId, oldItem.Id, false);
        var restored = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 3, 1));
        Assert.Equal(2, restored.ItemSnapshots.Count);
        Assert.All(restored.ItemSnapshots, entry => Assert.Null(entry.Amount));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ChangedCurrencyRequiresAnExplicitNewValueOrClear(bool provideValue, bool clear)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 100m)]);
        await using (var context = _database.CreateDbContext())
        {
            var tracked = await context.TrackedItems.SingleAsync();
            context.Entry(tracked).Property(entry => entry.CurrencyCode).CurrentValue = "ZAR";
            await context.SaveChangesAsync();
        }
        if (!provideValue && !clear)
        {
            await Assert.ThrowsAsync<Hearthcast.Data.Validation.InvalidValueException>(() => _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1)));
        }
        else
        {
            var capture = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1), [new(item.Id, clear ? null : 1_900m)]);
            var entry = Assert.Single(capture.ItemSnapshots);
            Assert.Equal("ZAR", entry.CurrencyCode);
            Assert.Equal(clear ? null : 1_900m, entry.Amount);
            Assert.Null(entry.CarriedFromItemSnapshotId);
        }
    }

    [Fact]
    public async Task CountryChangesDoNotChangeCurrencyAndUnknownConfirmationRemainsUnknown()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 100m)]);
        var source = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        await using (var context = _database.CreateDbContext())
        {
            var tracked = await context.TrackedItems.SingleAsync();
            context.Entry(tracked).Property(entry => entry.CountryCode).CurrentValue = "ZA";
            await context.SaveChangesAsync();
            // Represents imported provenance whose original confirmation date is unknown.
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ItemSnapshots\" SET \"LastConfirmedDate\" = NULL, \"LastConfirmedPrecision\" = NULL WHERE \"SnapshotId\" = {source.Id}");
        }
        var next = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 3, 1));
        var saved = Assert.Single((await _snapshots.GetAsync(setup.HouseholdId, next.Id)).ItemSnapshots);
        Assert.Equal(100m, saved.Amount);
        Assert.Equal("GBP", saved.CurrencyCode);
        Assert.Equal("ZA", saved.CountryCode);
        Assert.Null(saved.LastConfirmedDate);
        Assert.Null(saved.LastConfirmedPrecision);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, saved.ValueOrigin);
    }

    [Fact]
    public async Task CreateNextCanStartTheHistoryAndFreshCaptureDoesNotCarryImplicitly()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        var first = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 1, 1), [new(item.Id, 10m)]);
        var fresh = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 2, 1));
        Assert.Equal(10m, Assert.Single(first.ItemSnapshots).Amount);
        Assert.Null(Assert.Single(fresh.ItemSnapshots).Amount);
        var next = await _snapshots.CreateNextAsync(setup.HouseholdId, new DateOnly(2023, 3, 1));
        Assert.Null(Assert.Single(next.ItemSnapshots).Amount);
    }
}
