using Hearthcast.Data.Households;
using Hearthcast.Data.Persistence;
using Hearthcast.Data.Projections;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Hearthcast.Data.Tests.Projections;

public sealed class ProjectionExpenseTargetPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ProjectionPresetService Service => new(_database);
    private static ProjectionPresetSettings Settings => new("Scenario", "GBP", 20, 3, true,
        [new("GBP", 1000, 8, 100, 2)], new(40000, " gbp ", 4, false));
    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task TargetRoundTripsUpdatesAndClearsIndependentlyOfTheViewingCurrency()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var saved = await Service.CreateAsync(home.Id, Settings, [], []);
        var loaded = await Service.GetAsync(home.Id, saved.Id);
        Assert.Equal(new(40000, "GBP", 4, false), loaded.ExpenseTarget);
        var changed = Settings with { CurrencyCode = "ZAR", ExpenseTarget = new(30000, "GBP", 3, true) };
        await Service.UpdateAsync(home.Id, saved.Id, changed, [], []);
        loaded = await Service.GetAsync(home.Id, saved.Id);
        Assert.Equal("ZAR", loaded.CurrencyCode);
        Assert.Equal(changed.ExpenseTarget, loaded.ExpenseTarget);
        await Service.UpdateAsync(home.Id, saved.Id, changed with { ExpenseTarget = null }, [], []);
        loaded = await Service.GetAsync(home.Id, saved.Id);
        Assert.Null(loaded.ExpenseTarget);
        Assert.Null(loaded.AnnualExpenses);
        Assert.Null(loaded.ExpenseCurrencyCode);
        Assert.Null(loaded.AnnualWithdrawalRatePercent);
        Assert.False(loaded.IncludeSavingsInExpenseTarget);
        Assert.Single(loaded.Currencies);
    }

    [Fact]
    public async Task InvalidTargetReplacementDoesNotOverwriteAnyPresetValues()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var saved = await Service.CreateAsync(home.Id, Settings, [], [], isDefault: true);
        var before = await Service.GetAsync(home.Id, saved.Id);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateAsync(home.Id, saved.Id,
            Settings with { Name = "Changed", ExpenseTarget = new(1, "GBP", 0, true) }, [], []));
        var after = await Service.GetAsync(home.Id, saved.Id);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Equal(before.ExpenseTarget, after.ExpenseTarget);
        Assert.True(after.IsDefault);
    }

    [Theory]
    [InlineData("Partial")]
    [InlineData("Currency")]
    [InlineData("RateZero")]
    [InlineData("RateNull")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public async Task DatabaseRejectsPartialInvalidAndNonfiniteTargets(string problem)
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var saved = await Service.CreateAsync(home.Id, Settings, [], []);
        await using var context = _database.CreateDbContext();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => problem switch
        {
            "Partial" => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ProjectionPresets" SET "AnnualExpenses" = NULL WHERE "Id" = {saved.Id}"""),
            "Currency" => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ProjectionPresets" SET "ExpenseCurrencyCode" = 'gbp' WHERE "Id" = {saved.Id}"""),
            "RateZero" => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ProjectionPresets" SET "AnnualWithdrawalRatePercent" = 0 WHERE "Id" = {saved.Id}"""),
            "RateNull" => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ProjectionPresets" SET "AnnualWithdrawalRatePercent" = NULL WHERE "Id" = {saved.Id}"""),
            "NaN" => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ProjectionPresets" SET "AnnualExpenses" = 'NaN'::numeric WHERE "Id" = {saved.Id}"""),
            _ => context.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ProjectionPresets" SET "AnnualExpenses" = 'Infinity'::numeric WHERE "Id" = {saved.Id}"""),
        });
        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal("CK_ProjectionPresets_ExpenseTarget", failure.ConstraintName);
    }

    [Fact]
    public async Task MigrationPreservesExistingPresetsAndLeavesExpenseTargetsDisabled()
    {
        var home = await new HouseholdService(_database).CreateAsync("Example", "Alex");
        var saved = await Service.CreateAsync(home.Id, Settings with { ExpenseTarget = null }, [], [], isDefault: true);
        await using var context = _database.CreateDbContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261108101704_AddDefaultProjectionPresets");
        await migrator.MigrateAsync();
        var loaded = await Service.GetAsync(home.Id, saved.Id);
        Assert.Null(loaded.ExpenseTarget);
        Assert.False(loaded.IncludeSavingsInExpenseTarget);
        Assert.True(loaded.IsDefault);
        Assert.Equal(1000, Assert.Single(loaded.Currencies).MonthlyInvestmentContribution);
        Assert.Equal(saved.Name, loaded.Name);
        Assert.InRange((saved.CreatedAtUtc - loaded.CreatedAtUtc).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
    }
}
