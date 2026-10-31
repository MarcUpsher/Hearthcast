using System.Globalization;
using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Tests.TrackedItems;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class SnapshotPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private readonly SnapshotService _snapshots;
    private readonly TrackedItemService _items;

    public SnapshotPersistenceTests()
    {
        _snapshots = new SnapshotService(_database);
        _items = new TrackedItemService(_database);
    }

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task CaptureIncludesTheFullRosterAndActiveItemsWithIndependentHoldingsAndAllocations()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var household = await new HouseholdService(_database).AddPersonAsync(setup.HouseholdId, "Person C", false);
        var thirdPersonId = household.People.Single(person => person.DisplayName == "Person C").Id;
        var shared = await setup.CreateItemAsync(_items, allocations:
            [new(setup.HolderPersonId, 5_000), new(setup.OtherPersonId, 5_000)]);
        var personal = await _items.CreateAsync(setup.HouseholdId, setup.GroupId, thirdPersonId,
            "Item B", "ZAR", "ZA", false);
        var retired = await setup.CreateItemAsync(_items);
        await _items.SetRetiredAsync(setup.HouseholdId, retired.Id, true);

        var created = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 5, 19),
            [new(shared.Id, 123.45m), new(personal.Id, 60m)]);
        var saved = await _snapshots.GetAsync(setup.HouseholdId, created.Id);

        Assert.Equal(3, saved.People.Count);
        Assert.False(saved.People.Single(person => person.PersonId == thirdPersonId).ParticipatesInHouseholdFinances);
        Assert.Equal(2, saved.ItemSnapshots.Count);
        var entry = saved.ItemSnapshots.Single(item => item.TrackedItemId == shared.Id);
        Assert.Equal(setup.HolderPersonId, entry.HolderSnapshotPerson.PersonId);
        Assert.Equal(123.45m, entry.Amount);
        Assert.Equal("Category A", entry.CategoryName);
        Assert.Equal("Group A", entry.GroupName);
        Assert.Equal(FinancialType.Asset, entry.FinancialType);
        Assert.Equal("GBP", entry.CurrencyCode);
        Assert.Equal(2, entry.HouseholdAllocations.Count);
        Assert.All(entry.HouseholdAllocations, allocation => Assert.Equal(5_000, allocation.ShareBasisPoints));
        Assert.All(entry.HouseholdAllocations, allocation =>
            Assert.Same(saved.People.Single(person => person.Id == allocation.SnapshotPersonId), allocation.SnapshotPerson));
        var excluded = saved.ItemSnapshots.Single(item => item.TrackedItemId == personal.Id);
        Assert.Equal(thirdPersonId, excluded.HolderSnapshotPerson.PersonId);
        Assert.False(excluded.IncludeInHousehold);
        Assert.Empty(excluded.HouseholdAllocations);
        Assert.Equal("ZAR", excluded.CurrencyCode);
        Assert.Equal("ZA", excluded.CountryCode);
        Assert.Equal("Household A", saved.HouseholdName);
    }

    [Fact]
    public async Task OmittedAndNullValuesAreMissingWhileZeroIsConfirmed()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var omitted = await setup.CreateItemAsync(_items);
        var unknown = await setup.CreateItemAsync(_items);
        var zero = await setup.CreateItemAsync(_items);
        var date = new DateOnly(2023, 5, 19);
        var created = await _snapshots.CreateAsync(setup.HouseholdId, date, [new(unknown.Id, null), new(zero.Id, 0m)]);
        var saved = await _snapshots.GetAsync(setup.HouseholdId, created.Id);

        Assert.All(saved.ItemSnapshots.Where(item => item.TrackedItemId != zero.Id), item =>
        {
            Assert.Null(item.Amount);
            Assert.Equal(SnapshotValueOrigin.Unrecorded, item.ValueOrigin);
            Assert.Null(item.LastConfirmedDate);
            Assert.Null(item.LastConfirmedPrecision);
        });
        Assert.Contains(saved.ItemSnapshots, item => item.TrackedItemId == omitted.Id);
        var confirmed = saved.ItemSnapshots.Single(item => item.TrackedItemId == zero.Id);
        Assert.Equal(0m, confirmed.Amount);
        Assert.Equal(SnapshotValueOrigin.Confirmed, confirmed.ValueOrigin);
        Assert.Equal(date, confirmed.LastConfirmedDate);
        Assert.Equal(DatePrecision.Day, confirmed.LastConfirmedPrecision);
    }

    [Theory]
    [InlineData(DatePrecision.Day, 19)]
    [InlineData(DatePrecision.Month, 1)]
    public async Task FinancialDateAndPrecisionAreIndependentOfEntryTime(DatePrecision precision, int expectedDay)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        var before = DateTimeOffset.UtcNow;
        var created = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 5, 19),
            [new(item.Id, 1m)], precision);
        var after = DateTimeOffset.UtcNow;
        var saved = await _snapshots.GetAsync(setup.HouseholdId, created.Id);

        Assert.Equal(new DateOnly(2023, 5, expectedDay), saved.SnapshotDate);
        Assert.Equal(precision, saved.DatePrecision);
        Assert.Equal(saved.SnapshotDate, Assert.Single(saved.ItemSnapshots).LastConfirmedDate);
        Assert.Equal(precision, Assert.Single(saved.ItemSnapshots).LastConfirmedPrecision);
        Assert.InRange(saved.CreatedAtUtc, before, after);
        Assert.Equal(TimeSpan.Zero, saved.CreatedAtUtc.Offset);
        Assert.Equal(saved.CreatedAtUtc, saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task AHouseholdWithoutItemsCanBeCapturedAndDefaultsToToday()
    {
        var household = await new HouseholdService(_database).CreateAsync("Household A", "Person A");
        var before = DateOnly.FromDateTime(DateTime.Today);
        var created = await _snapshots.CreateAsync(household.Id);
        var after = DateOnly.FromDateTime(DateTime.Today);
        var saved = await _snapshots.GetAsync(household.Id, created.Id);
        Assert.InRange(saved.SnapshotDate, before, after);
        Assert.Equal(DatePrecision.Day, saved.DatePrecision);
        Assert.Single(saved.People);
        Assert.Empty(saved.ItemSnapshots);
    }

    [Fact]
    public async Task SavedMetadataSurvivesChangesToTheCurrentSetup()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items, allocations:
            [new(setup.HolderPersonId, 5_000), new(setup.OtherPersonId, 5_000)]);
        var created = await _snapshots.CreateAsync(setup.HouseholdId, values: [new(item.Id, 123m)]);
        await _items.SetHouseholdAllocationAsync(setup.HouseholdId, item.Id, false);
        await _items.SetRetiredAsync(setup.HouseholdId, item.Id, true);
        await new HouseholdService(_database).SetPersonParticipationAsync(setup.HouseholdId, setup.OtherPersonId, false);
        await using (var context = _database.CreateDbContext())
        {
            (await context.Households.SingleAsync()).Rename("Renamed household");
            (await context.People.SingleAsync(person => person.Id == setup.HolderPersonId)).Rename("Renamed person");
            (await context.Categories.SingleAsync()).Rename("Renamed category");
            (await context.ItemGroups.SingleAsync()).Rename("Renamed group");
            (await context.TrackedItems.SingleAsync()).Rename("Renamed item");
            await context.SaveChangesAsync();
        }

        var saved = await _snapshots.GetAsync(setup.HouseholdId, created.Id);
        var entry = Assert.Single(saved.ItemSnapshots);
        Assert.Equal("Household A", saved.HouseholdName);
        Assert.Equal("Person A", entry.HolderSnapshotPerson.DisplayName);
        Assert.Equal("Category A", entry.CategoryName);
        Assert.Equal("Group A", entry.GroupName);
        Assert.Equal("Item A", entry.ItemName);
        Assert.True(entry.IncludeInHousehold);
        Assert.Equal(123m, entry.Amount);
        Assert.Equal(2, entry.HouseholdAllocations.Count);
        Assert.True(saved.People.Single(person => person.PersonId == setup.OtherPersonId).ParticipatesInHouseholdFinances);

        var next = await _snapshots.CreateAsync(setup.HouseholdId);
        Assert.Equal("Renamed household", next.HouseholdName);
        Assert.Empty(next.ItemSnapshots);
        Assert.False(next.People.Single(person => person.PersonId == setup.OtherPersonId).ParticipatesInHouseholdFinances);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("123456789.1234567890123456789")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001")]
    public async Task MonetaryValuesRoundTripExactlyAsNativeNumeric(string amountText)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        var amount = decimal.Parse(amountText, CultureInfo.InvariantCulture);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var created = await _snapshots.CreateAsync(setup.HouseholdId, values: [new(item.Id, amount)]);
            var saved = await _snapshots.GetAsync(setup.HouseholdId, created.Id);
            Assert.Equal(amount, Assert.Single(saved.ItemSnapshots).Amount);

            await using var context = _database.CreateDbContext();
            await context.Database.OpenConnectionAsync();
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_typeof(\"Amount\")::text, \"Amount\" FROM \"ItemSnapshots\"";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("numeric", reader.GetString(0));
            Assert.Equal(amount, reader.GetDecimal(1));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("unknown")]
    [InlineData("foreign")]
    [InlineData("retired")]
    [InlineData("precision")]
    public async Task InvalidCapturesLeaveNoPartialSnapshot(string scenario)
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var item = await setup.CreateItemAsync(_items);
        ItemSnapshotValue[] values = [new(item.Id, 10m)];
        var precision = DatePrecision.Day;
        switch (scenario)
        {
            case "duplicate": values = [new(item.Id, 10m), new(item.Id, 20m)]; break;
            case "negative": values = [new(item.Id, -0.01m)]; break;
            case "unknown": values = [new(Guid.NewGuid(), 10m)]; break;
            case "foreign":
                var other = await TrackedItemTestSetup.CreateAsync(_database);
                values = [new((await other.CreateItemAsync(_items)).Id, 10m)];
                break;
            case "retired": await _items.SetRetiredAsync(setup.HouseholdId, item.Id, true); break;
            case "precision": precision = (DatePrecision)99; break;
        }

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _snapshots.CreateAsync(setup.HouseholdId, values: values, datePrecision: precision));
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.Snapshots.ToListAsync());
        Assert.Empty(await context.SnapshotPeople.ToListAsync());
        Assert.Empty(await context.ItemSnapshots.ToListAsync());
        Assert.Empty(await context.ItemSnapshotHouseholdAllocations.ToListAsync());
    }

    [Fact]
    public async Task HouseholdBoundariesApplyToCreationAndRetrieval()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var created = await _snapshots.CreateAsync(setup.HouseholdId);
        var other = await new HouseholdService(_database).CreateAsync("Household B", "Person C");
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => _snapshots.CreateAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => _snapshots.GetAsync(other.Id, created.Id));
        await Assert.ThrowsAsync<Hearthcast.Data.Validation.ResourceNotFoundException>(() => _snapshots.GetAsync(setup.HouseholdId, Guid.NewGuid()));
    }

    [Fact]
    public async Task MultipleCapturesInOneMonthRemainSeparate()
    {
        var setup = await TrackedItemTestSetup.CreateAsync(_database);
        var first = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 5, 1));
        var second = await _snapshots.CreateAsync(setup.HouseholdId, new DateOnly(2023, 5, 1));
        Assert.NotEqual(first.Id, second.Id);
        await using var context = _database.CreateDbContext();
        Assert.Equal(2, await context.Snapshots.CountAsync(snapshot => snapshot.SnapshotDate == new DateOnly(2023, 5, 1)));
    }

}
