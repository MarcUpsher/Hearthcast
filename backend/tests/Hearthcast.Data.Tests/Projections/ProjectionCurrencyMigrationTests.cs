using Hearthcast.Data.Households;
using Hearthcast.Data.Projections;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.Projections;

public sealed class ProjectionCurrencyMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261107083436_AddProjectionPresets";
    private readonly PostgresTestDatabase _database = new();
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpgradePreservesExistingSingleCurrencyAmountsRatesAndNativeLoanPayment(bool selectedNativeGroup)
    {
        var d = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await d.CreateAssetAsync(new(_database));
        Hearthcast.Data.Snapshots.Snapshot? historical = null;
        if (selectedNativeGroup)
        {
            var items = new Hearthcast.Data.TrackedItems.TrackedItemService(_database);
            var item = await items.CreateAsync(d.HouseholdId, d.GroupId, d.FirstPersonId, "Historical fund", "ZAR", "ZA", true);
            historical = await new Hearthcast.Data.Snapshots.SnapshotService(_database).CreateAsync(d.HouseholdId,
                new DateOnly(2020, 1, 1), [new(item.Id, 100)]);
            await items.SetHouseholdAllocationAsync(d.HouseholdId, item.Id, false);
        }
        var id = Guid.NewGuid();
        await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        var now = DateTimeOffset.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProjectionPresets" ("Id", "HouseholdId", "Name", "CurrencyCode", "DurationYears",
                "MonthlyInvestmentContribution", "AnnualInvestmentReturnPercent", "MonthlySavingsContribution",
                "AnnualSavingsInterestPercent", "AnnualInflationPercent", "ShowInflationAdjustedValues", "CreatedAtUtc", "UpdatedAtUtc")
            VALUES ({id}, {d.HouseholdId}, {"Baseline"}, {"GBP"}, {10}, {500m}, {6m}, {100m}, {2m}, {3m}, {true}, {now}, {now});
            INSERT INTO "ProjectionPresetFinancedAssets" ("ProjectionPresetId", "HouseholdId", "FinancedAssetId",
                "MonthlyLoanPaymentInAssetCurrency", "AnnualLoanInterestPercent", "AnnualAssetAppreciationPercent")
            VALUES ({id}, {d.HouseholdId}, {asset.Id}, {20000m}, {9m}, {2m});
            """);
        if (selectedNativeGroup)
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ProjectionPresetGroups" ("ProjectionPresetId", "HouseholdId", "GroupId", "Bucket")
                VALUES ({id}, {d.HouseholdId}, {d.GroupId}, {1});
                """);
        await migrator.MigrateAsync();
        var saved = await new ProjectionPresetService(_database).GetAsync(d.HouseholdId, id);
        Assert.Equal(selectedNativeGroup ? 2 : 1, saved.Currencies.Count);
        var pot = saved.Currencies.Single(c => c.CurrencyCode == "GBP");
        Assert.Equal("GBP", pot.CurrencyCode);
        Assert.Equal(500, pot.MonthlyInvestmentContribution);
        Assert.Equal(6, pot.AnnualInvestmentReturnPercent);
        Assert.Equal(100, pot.MonthlySavingsContribution);
        Assert.Equal(2, pot.AnnualSavingsInterestPercent);
        var loan = Assert.Single(saved.FinancedAssets);
        Assert.Equal(20000, loan.MonthlyLoanPaymentInAssetCurrency);
        Assert.Equal(0, loan.ExtraMonthlyLoanPaymentInAssetCurrency);
        Assert.Equal("GBP", loan.InvestmentCurrencyAfterPayoff);
        if (selectedNativeGroup)
        {
            var native = saved.Currencies.Single(c => c.CurrencyCode == "ZAR");
            Assert.Equal(0, native.MonthlyInvestmentContribution);
            Assert.Equal(0, native.MonthlySavingsContribution);
            Assert.Equal(6, native.AnnualInvestmentReturnPercent);
            Assert.Equal(2, native.AnnualSavingsInterestPercent);
            var after = await new Hearthcast.Data.Snapshots.SnapshotService(_database).GetAsync(d.HouseholdId, historical!.Id);
            Assert.Equal(historical.Revision, after.Revision);
            Assert.Equal(100, Assert.Single(after.ItemSnapshots).Amount);
            return;
        }
        await migrator.MigrateAsync(Previous);
        var oldAmount = await context.Database.SqlQuery<decimal>($"""
            SELECT "MonthlyInvestmentContribution" AS "Value" FROM "ProjectionPresets" WHERE "Id" = {id}
            """).SingleAsync();
        Assert.Equal(500, oldAmount);
    }

    [Fact]
    public async Task DowngradeRefusesToDiscardMultipleCurrencyPots()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var service = new ProjectionPresetService(_database);
        var saved = await service.CreateAsync(home.Id, new("Baseline", "GBP", 10, 3, false,
            [new("GBP", 1000, 5, 0, 2), new("ZAR", 500, 8, 100, 4)]), [], []);
        await using var context = _database.CreateDbContext();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Contains("Cannot downgrade", failure.MessageText);
        Assert.Equal(2, (await service.GetAsync(home.Id, saved.Id)).Currencies.Count);
    }
    [Fact]
    public async Task DowngradeRefusesExtraPaymentsEvenWithOneViewingCurrencyPotAndPreservesState()
    {
        var d = await FinancedAssetTestSetup.CreateAsync(_database);
        var asset = await d.CreateAssetAsync(new(_database));
        var service = new ProjectionPresetService(_database);
        var saved = await service.CreateAsync(d.HouseholdId,
            new("Extra payment", "GBP", 10, 3, false, [new("GBP", 1000, 5, 0, 2)]), [],
            [new(asset.Id, 20000, 9, 2, 2000, "GBP")]);
        Assert.Equal(saved.CurrencyCode, Assert.Single(saved.Currencies).CurrencyCode);
        Assert.Equal(saved.CurrencyCode, Assert.Single(saved.FinancedAssets).InvestmentCurrencyAfterPayoff);
        await using var context = _database.CreateDbContext();
        var appliedBefore = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Contains("Cannot downgrade", failure.MessageText);
        Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        var after = await service.GetAsync(d.HouseholdId, saved.Id);
        Assert.Equal("GBP", Assert.Single(after.Currencies).CurrencyCode);
        var loan = Assert.Single(after.FinancedAssets);
        Assert.Equal(20000, loan.MonthlyLoanPaymentInAssetCurrency);
        Assert.Equal(2000, loan.ExtraMonthlyLoanPaymentInAssetCurrency);
        Assert.Equal("GBP", loan.InvestmentCurrencyAfterPayoff);
        Assert.Equal(saved.UpdatedAtUtc.ToUnixTimeMilliseconds(), after.UpdatedAtUtc.ToUnixTimeMilliseconds());
    }

}
