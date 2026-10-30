using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Snapshots;

public sealed class HouseholdHistoryTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AHouseholdHistoryPreservesBalancesIdentityAndReportingContextAcrossFifteenYears()
    {
        var data = await HouseholdHistoryTestData.CreateAsync(_database);
        var snapshots = new SnapshotService(_database);
        var items = new TrackedItemService(_database);
        var households = new HouseholdService(_database);
        var january = await snapshots.CreateAsync(data.HouseholdId, new DateOnly(2015, 1, 20),
            [new(data.RetirementId, 10_000m), new(data.PensionId, 8_000m), new(data.EtfAlphaId, 2_000m)], DatePrecision.Month);
        Assert.Equal(3, january.People.Count);
        Assert.DoesNotContain(january.ItemSnapshots, item => item.HolderSnapshotPerson.PersonId == data.ChildId);
        var march = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2015, 3, 1),
            [new(data.RetirementId, 11_000m)], DatePrecision.Month);
        AssertCarried(march, data.PensionId, 8_000m, new DateOnly(2015, 1, 1), Entry(january, data.PensionId).Id);
        var april = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2015, 4, 1),
            [new(data.RetirementId, 11_000m)], DatePrecision.Month);
        Assert.Equal(SnapshotValueOrigin.Confirmed, Entry(april, data.RetirementId).ValueOrigin);
        Assert.Equal(new DateOnly(2015, 4, 1), Entry(april, data.RetirementId).LastConfirmedDate);
        AssertCarried(april, data.PensionId, 8_000m, new DateOnly(2015, 1, 1), Entry(march, data.PensionId).Id);

        await data.AddUkItemsAsync(_database);
        var uk = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2020, 2, 1), data.UkValues(), DatePrecision.Month);
        Assert.Equal(9, uk.ItemSnapshots.Count);
        Assert.Equal("ZAR", Entry(uk, data.RetirementId).CurrencyCode);
        Assert.Equal("GBP", Entry(uk, data.EmergencyId).CurrencyCode);
        var closing = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2021, 3, 1),
            [new(data.EtfAlphaId, 0m)], DatePrecision.Month);
        await data.ReplaceEtfAsync(_database);
        var replacement = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2021, 4, 1),
            [new(data.EtfBetaId, 25_000m)], DatePrecision.Month);
        Assert.Equal(0m, Entry(closing, data.EtfAlphaId).Amount);
        Assert.DoesNotContain(replacement.ItemSnapshots, item => item.TrackedItemId == data.EtfAlphaId);
        Assert.NotEqual(data.EtfAlphaId, data.EtfBetaId);
        Assert.Null(Entry(replacement, data.EtfBetaId).CarriedFromItemSnapshotId);

        var reference = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 1, 1),
            data.ReferenceValues(), DatePrecision.Month);
        var savedReference = await snapshots.GetAsync(data.HouseholdId, reference.Id);
        Assert.Equal(9, savedReference.ItemSnapshots.Count);
        foreach (var expected in data.ReferenceValues())
        {
            Assert.Equal(expected.Amount, Entry(savedReference, expected.TrackedItemId).Amount);
            Assert.Equal(SnapshotValueOrigin.Confirmed, Entry(savedReference, expected.TrackedItemId).ValueOrigin);
        }
        Assert.Equal(FinancialType.Debt, Entry(savedReference, data.LoanId).FinancialType);
        Assert.All(savedReference.ItemSnapshots.Where(item => item.TrackedItemId != data.LoanId),
            item => Assert.Equal(FinancialType.Asset, item.FinancialType));
        var emergency = Entry(savedReference, data.EmergencyId);
        Assert.Equal(data.AdultBId, emergency.HolderSnapshotPerson.PersonId);
        Assert.Equal(2, emergency.HouseholdAllocations.Count);
        Assert.All(emergency.HouseholdAllocations, share => Assert.Equal(5_000, share.ShareBasisPoints));
        Assert.False(Entry(savedReference, data.ChildSavingsId).IncludeInHousehold);
        Assert.False(Entry(savedReference, data.PersonalReserveId).IncludeInHousehold);

        var dated = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 1, 18), sourceSnapshotId: reference.Id);
        AssertCarried(dated, data.DepositId, 0m, new DateOnly(2023, 1, 1), Entry(reference, data.DepositId).Id);
        await items.SetHouseholdAllocationAsync(data.HouseholdId, data.EmergencyId, true,
            [new(data.AdultAId, 7_000), new(data.AdultBId, 3_000)]);
        await using (var context = _database.CreateDbContext())
        {
            (await context.Households.SingleAsync()).Rename("Cedar updated");
            (await context.People.SingleAsync(person => person.Id == data.AdultAId)).Rename("Adult A updated");
            (await context.Categories.SingleAsync(category => category.Id == data.InvestmentsCategoryId)).Rename("Long-term assets");
            (await context.ItemGroups.SingleAsync(group => group.Id == data.IsaGroupId)).Rename("Tax wrapper");
            (await context.TrackedItems.SingleAsync(item => item.Id == data.EtfBetaId)).Rename("ETF Beta updated");
            await context.SaveChangesAsync();
        }
        var june = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2023, 6, 1),
            datePrecision: DatePrecision.Month, sourceSnapshotId: dated.Id);
        var juneEmergency = Entry(june, data.EmergencyId);
        Assert.Equal(data.AdultBId, juneEmergency.HolderSnapshotPerson.PersonId);
        Assert.Equal(7_000, juneEmergency.HouseholdAllocations.Single(share => share.SnapshotPerson.PersonId == data.AdultAId).ShareBasisPoints);
        Assert.Equal("Long-term assets", Entry(june, data.IsaId).CategoryName);
        Assert.Equal("Tax wrapper", Entry(june, data.IsaId).GroupName);
        Assert.Equal("ETF Beta updated", Entry(june, data.EtfBetaId).ItemName);
        Assert.Equal("Adult A updated", Entry(june, data.IsaId).HolderSnapshotPerson.DisplayName);

        var later = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2024, 2, 1), datePrecision: DatePrecision.Month);
        var corrected = await snapshots.CorrectValuesAsync(data.HouseholdId, reference.Id, reference.Revision, [new(data.IsaId, 900m)]);
        var historical = await snapshots.GetAsync(data.HouseholdId, reference.Id);
        Assert.Equal(900m, Entry(historical, data.IsaId).Amount);
        Assert.NotEqual(reference.Revision, corrected.Revision);
        Assert.Equal("Cedar", historical.HouseholdName);
        Assert.Equal("Adult A", Entry(historical, data.IsaId).HolderSnapshotPerson.DisplayName);
        Assert.Equal("Investments", Entry(historical, data.IsaId).CategoryName);
        Assert.Equal("ISA", Entry(historical, data.IsaId).GroupName);
        Assert.All(Entry(historical, data.EmergencyId).HouseholdAllocations, share => Assert.Equal(5_000, share.ShareBasisPoints));
        var unchanged = await snapshots.GetAsync(data.HouseholdId, later.Id);
        AssertCarried(unchanged, data.IsaId, 1_000m, new DateOnly(2023, 1, 1), Entry(june, data.IsaId).Id);
        Assert.Equal(later.Revision, unchanged.Revision);

        await households.SetPersonParticipationAsync(data.HouseholdId, data.ChildId, true);
        var september = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2030, 9, 1), datePrecision: DatePrecision.Month);
        Assert.True(september.People.Single(person => person.PersonId == data.ChildId).ParticipatesInHouseholdFinances);
        Assert.False(Entry(september, data.ChildSavingsId).IncludeInHousehold);
        await items.SetHouseholdAllocationAsync(data.HouseholdId, data.ChildSavingsId, true);
        var october = await snapshots.CreateNextAsync(data.HouseholdId, new DateOnly(2030, 10, 1), datePrecision: DatePrecision.Month);
        Assert.True(Entry(october, data.ChildSavingsId).IncludeInHousehold);
        Assert.Equal(data.ChildId, Assert.Single(Entry(october, data.ChildSavingsId).HouseholdAllocations).SnapshotPerson.PersonId);
        Assert.False(historical.People.Single(person => person.PersonId == data.ChildId).ParticipatesInHouseholdFinances);
        Assert.Equal(300m, Entry(october, data.ChildSavingsId).Amount);
        AssertCarried(october, data.IsaId, 1_000m, new DateOnly(2023, 1, 1), Entry(september, data.IsaId).Id);

        await using var verification = _database.CreateDbContext();
        Assert.False(await verification.Snapshots.AnyAsync(snapshot => snapshot.SnapshotDate == new DateOnly(2015, 2, 1)));
        Assert.Equal(12, await verification.Snapshots.CountAsync());
        Assert.Equal(0m, (await snapshots.GetAsync(data.HouseholdId, closing.Id)).ItemSnapshots.Single(item => item.TrackedItemId == data.EtfAlphaId).Amount);
    }

    private static ItemSnapshot Entry(Snapshot snapshot, Guid itemId) => snapshot.ItemSnapshots.Single(item => item.TrackedItemId == itemId);

    private static void AssertCarried(Snapshot snapshot, Guid itemId, decimal amount, DateOnly confirmedDate, Guid sourceEntryId)
    {
        var entry = Entry(snapshot, itemId);
        Assert.Equal(amount, entry.Amount);
        Assert.Equal(SnapshotValueOrigin.CarriedForward, entry.ValueOrigin);
        Assert.Equal(confirmedDate, entry.LastConfirmedDate);
        Assert.Equal(DatePrecision.Month, entry.LastConfirmedPrecision);
        Assert.Equal(sourceEntryId, entry.CarriedFromItemSnapshotId);
    }
}
