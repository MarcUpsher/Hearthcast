using System.Text.Json;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Summaries;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.HistoryImport;
using Microsoft.EntityFrameworkCore;

namespace Hearthcast.Data.Tests.Importing;

public sealed class HistoryImportTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task ImportsExplicitMembershipCarrySourcesNamesAndSharesAndRefusesARepeat()
    {
        var (file,totals) = Example();
        await using var context = _database.CreateDbContext();
        var result = await HistoryLoader.ImportAsync(context,file,totals);
        Assert.Equal(2,result.SnapshotCount);
        Assert.Equal(2,result.CurrencyMonthChecks);
        var service = new SnapshotService(_database);
        var inventory = await service.ListAsync(result.HouseholdId);
        var first = await service.GetAsync(result.HouseholdId,inventory.Single(s => s.SnapshotDate.Month==1).Id);
        var second = await service.GetAsync(result.HouseholdId,inventory.Single(s => s.SnapshotDate.Month==3).Id);
        var reserve = second.ItemSnapshots.Single(i => i.ItemName=="Emergency fund");
        Assert.Equal(100m,reserve.Amount);
        Assert.Equal(SnapshotValueOrigin.CarriedForward,reserve.ValueOrigin);
        Assert.Equal(new DateOnly(2020,1,1),reserve.LastConfirmedDate);
        Assert.Equal(first.ItemSnapshots.Single(i => i.TrackedItemId==reserve.TrackedItemId).Id,reserve.CarriedFromItemSnapshotId);
        Assert.Equal(10_000,Assert.Single(reserve.HouseholdAllocations).ShareBasisPoints);
        Assert.All(first.ItemSnapshots.Single(i => i.TrackedItemId==reserve.TrackedItemId).HouseholdAllocations,a => Assert.Equal(5_000,a.ShareBasisPoints));
        Assert.Equal(320m,second.FinancedAssetSnapshots.Single().CurrentValuation);
        Assert.Equal(SnapshotValueOrigin.CarriedForward,second.FinancedAssetSnapshots.Single().OutstandingLoanBalanceValueOrigin);
        var household = await new HouseholdSummaryService(_database).GetAsync(result.HouseholdId,second.Id);
        Assert.Equal(220m,Assert.Single(household.Currencies).Totals.KnownNetPosition);
        var full = await new SnapshotSummaryService(_database).GetAsync(result.HouseholdId,second.Id);
        Assert.Equal(235m,Assert.Single(full.Currencies).AllRecordedTotals.KnownNetPosition);
        var individual = await new IndividualHoldingsSummaryService(_database).GetAsync(result.HouseholdId,second.Id);
        Assert.Equal(160m,individual.People.Single(p => p.Person.DisplayName=="Alex").Currencies.Single().HoldingsTotals.KnownNetPosition);
        Assert.All(await context.TrackedItems.AsNoTracking().ToArrayAsync(),i => Assert.True(i.IsRetired));
        Assert.Equal("Reserve",(await context.TrackedItems.AsNoTracking().SingleAsync(i => i.Id==reserve.TrackedItemId)).Name);
        await using var repeat = _database.CreateDbContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => HistoryLoader.ImportAsync(repeat,file,totals));
        Assert.Equal(2,await repeat.Snapshots.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReadbackVerificationRollsBackEveryInsertedRow(bool wrongCarryDate)
    {
        var (file,totals) = Example();
        if (wrongCarryDate)
            file.Tables.ItemValues[2] = file.Tables.ItemValues[2] with { LastConfirmedDate=new DateOnly(2020,3,1) };
        else totals[1] = totals[1] with { Household=999m };
        await using (var context = _database.CreateDbContext())
            await Assert.ThrowsAsync<InvalidDataException>(() => HistoryLoader.ImportAsync(context,file,totals));
        await using var verify = _database.CreateDbContext();
        Assert.False(await verify.Households.AnyAsync());
        Assert.False(await verify.Categories.AnyAsync());
        Assert.False(await verify.TrackedItems.AnyAsync());
        Assert.False(await verify.FinancedAssets.AnyAsync());
        Assert.False(await verify.Snapshots.AnyAsync());
        Assert.False(await verify.ItemSnapshots.AnyAsync());
        Assert.False(await verify.FinancedAssetSnapshots.AnyAsync());
    }

    [Theory]
    [InlineData("\"0.00000000000000000000000000001\"")]
    [InlineData("\"79228162514264337593543950336\"")]
    [InlineData("\"12345678901234567890123456789.12\"")]
    [InlineData("\"1e2\"")]
    public void RejectsDecimalsThatWouldRequireRounding(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<decimal>(json,HistoryFile.JsonOptions));

    private static (HistoryFile,ExpectedTotals[]) Example()
    {
        var file = new HistoryFile(2,"HistoricalSnapshots",new(
            [new("home","Example household")],
            [new("a","home","Alex",true),new("b","home","Morgan",true),new("c","home","Child",false)],
            [new("invest","home","Investments",Data.Classification.FinancialType.Asset),new("property","home","Property",Data.Classification.FinancialType.Asset)],
            [new("cash","invest","Cash"),new("house","property","House")],
            [new("reserve","cash","a","Reserve","GBP","GB",true,true),new("child","cash","c","Child savings","GBP","GB",false,true)],
            [new("house","house","Home","GBP","GB",null,null,200m,true,true,true)],
            [new("Item","reserve",null,"Household","a",5000),new("Item","reserve",null,"Household","b",5000),
                new("Item","reserve","mar","Household","b",10000),
                new("FinancedAsset","house",null,"Owner","a",5000),new("FinancedAsset","house",null,"Owner","b",5000),
                new("FinancedAsset","house",null,"Household","a",5000),new("FinancedAsset","house",null,"Household","b",5000)],
            [new("jan","home",new(2020,1,1),DatePrecision.Month),new("mar","home",new(2020,3,1),DatePrecision.Month)],
            [new("jan","reserve","Emergency fund",100m,SnapshotValueOrigin.Confirmed,new(2020,1,1),DatePrecision.Month,null),
                new("jan","child",null,10m,SnapshotValueOrigin.Confirmed,new(2020,1,1),DatePrecision.Month,null),
                new("mar","reserve","Emergency fund",100m,SnapshotValueOrigin.CarriedForward,new(2020,1,1),DatePrecision.Month,"jan"),
                new("mar","child",null,15m,SnapshotValueOrigin.Confirmed,new(2020,3,1),DatePrecision.Month,null)],
            [new("jan","house",FinancedAssetBalanceKind.CurrentValuation,300m,SnapshotValueOrigin.Confirmed,new(2020,1,1),DatePrecision.Month,null),
                new("jan","house",FinancedAssetBalanceKind.OutstandingLoanBalance,200m,SnapshotValueOrigin.Confirmed,new(2020,1,1),DatePrecision.Month,null),
                new("jan","house",FinancedAssetBalanceKind.AvailableToWithdraw,50m,SnapshotValueOrigin.Confirmed,new(2020,1,1),DatePrecision.Month,null),
                new("mar","house",FinancedAssetBalanceKind.CurrentValuation,320m,SnapshotValueOrigin.Confirmed,new(2020,3,1),DatePrecision.Month,null),
                new("mar","house",FinancedAssetBalanceKind.OutstandingLoanBalance,200m,SnapshotValueOrigin.CarriedForward,new(2020,1,1),DatePrecision.Month,"jan"),
                new("mar","house",FinancedAssetBalanceKind.AvailableToWithdraw,50m,SnapshotValueOrigin.CarriedForward,new(2020,1,1),DatePrecision.Month,"jan")]));
        ExpectedTotals[] totals = [new("jan","GBP",210m,200m,10m,new() { ["a"]=150m,["b"]=50m,["c"]=10m },
            new() { ["a"]=100m,["b"]=100m },new() { ["invest"]=100m,["property"]=100m },new() { ["cash"]=100m,["house"]=100m },0),
            new("mar","GBP",235m,220m,15m,new() { ["a"]=160m,["b"]=60m,["c"]=15m },
            new() { ["a"]=60m,["b"]=160m },new() { ["invest"]=100m,["property"]=120m },new() { ["cash"]=100m,["house"]=120m },2)];
        return (file,totals);
    }
}
