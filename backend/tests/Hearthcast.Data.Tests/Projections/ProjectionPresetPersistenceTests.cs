using Hearthcast.Data.Classification;
using Hearthcast.Data.FinancedAssets;
using Hearthcast.Data.Households;
using Hearthcast.Data.Projections;
using Hearthcast.Data.Tests.FinancedAssets;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hearthcast.Data.Tests.Projections;

public sealed class ProjectionPresetPersistenceTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private ProjectionPresetService Service => new(_database);
    private static ProjectionPresetSettings Settings(string name = "Baseline") =>
        new(name, "GBP", 10, 500m, 6m, 100m, 2m, 3m, true);

    public Task InitializeAsync() => _database.InitializeAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    private async Task<(Guid Household, Guid Group, Guid Asset, Guid SecondAsset)> Setup()
    {
        var setup = await FinancedAssetTestSetup.CreateAsync(_database);
        var service = new FinancedAssetService(_database);
        var first = await setup.CreateAssetAsync(service, name: "Home");
        var second = await setup.CreateAssetAsync(service, name: "Other asset", currency: "GBP", country: "GB");
        return (setup.HouseholdId, setup.GroupId, first.Id, second.Id);
    }

    [Fact]
    public async Task RoundTripKeepsIndependentLoansAndExactAssumptionsWithoutASnapshot()
    {
        var data = await Setup();
        var settings = Settings(" Big saving ") with { CurrencyCode = " gbp ", AnnualInvestmentReturnPercent = -2.125m };
        var saved = await Service.CreateAsync(data.Household, settings,
            [new(data.Group, ProjectionBucket.Investment)],
            [new(data.Asset, 1000m, 9.25m, -1m), new(data.SecondAsset, 200m, 0m, 3m)]);
        var loaded = await Service.GetAsync(data.Household, saved.Id);
        Assert.Equal(settings with { Name = "Big saving", CurrencyCode = "GBP" },
            new(loaded.Name, loaded.CurrencyCode, loaded.DurationYears, loaded.MonthlyInvestmentContribution,
                loaded.AnnualInvestmentReturnPercent, loaded.MonthlySavingsContribution,
                loaded.AnnualSavingsInterestPercent, loaded.AnnualInflationPercent, loaded.ShowInflationAdjustedValues));
        Assert.InRange((saved.CreatedAtUtc - loaded.CreatedAtUtc).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
        Assert.True(loaded.UpdatedAtUtc >= loaded.CreatedAtUtc);
        Assert.Equal(ProjectionBucket.Investment, Assert.Single(loaded.Groups).Bucket);
        Assert.Equal(1000m, loaded.FinancedAssets.Single(a => a.FinancedAssetId == data.Asset).MonthlyLoanPaymentInAssetCurrency);
        Assert.Equal(0m, loaded.FinancedAssets.Single(a => a.FinancedAssetId == data.SecondAsset).AnnualLoanInterestPercent);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.Snapshots.ToArrayAsync());
    }

    [Fact]
    public async Task UpdateChangesBucketsAndLoanSettingsAndRemovesOmittedSelections()
    {
        var d = await Setup();
        var saved = await Service.CreateAsync(d.Household, Settings(), [new(d.Group, ProjectionBucket.Investment)],
            [new(d.Asset, 1000, 9, 2), new(d.SecondAsset, 200, 5, 1)]);
        await Service.UpdateAsync(d.Household, saved.Id, Settings("Changed") with { DurationYears = 25 },
            [new(d.Group, ProjectionBucket.Savings)], [new(d.Asset, 1200, 8, 3)]);
        var loaded = await Service.GetAsync(d.Household, saved.Id);
        Assert.InRange((saved.CreatedAtUtc - loaded.CreatedAtUtc).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
        Assert.Equal(25, loaded.DurationYears);
        Assert.Equal(ProjectionBucket.Savings, Assert.Single(loaded.Groups).Bucket);
        Assert.Equal(1200, Assert.Single(loaded.FinancedAssets).MonthlyLoanPaymentInAssetCurrency);
        await Service.UpdateAsync(d.Household, saved.Id, Settings(), [], []);
        loaded = await Service.GetAsync(d.Household, saved.Id);
        Assert.Empty(loaded.Groups);
        Assert.Empty(loaded.FinancedAssets);
    }

    [Fact]
    public async Task InvalidReplacementLeavesTheWholePresetUnchanged()
    {
        var d = await Setup();
        var saved = await Service.CreateAsync(d.Household, Settings(), [new(d.Group, ProjectionBucket.Investment)],
            [new(d.Asset, 1000, 9, 2)]);
        var before = await Service.GetAsync(d.Household, saved.Id);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.UpdateAsync(d.Household, saved.Id,
            Settings("Changed"), [], [new(Guid.NewGuid(), 100, 2, 1)]));
        var loaded = await Service.GetAsync(d.Household, saved.Id);
        Assert.Equal("Baseline", loaded.Name);
        Assert.Single(loaded.Groups);
        Assert.Single(loaded.FinancedAssets);
        Assert.Equal(before.UpdatedAtUtc, loaded.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullSelectionEntriesAreRejectedWithoutChangingThePreset(bool nullAsset)
    {
        var d = await Setup();
        var saved = await Service.CreateAsync(d.Household, Settings(), [new(d.Group, ProjectionBucket.Investment)],
            [new(d.Asset, 1000, 9, 2)]);
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.UpdateAsync(d.Household, saved.Id,
            Settings("Changed"), nullAsset ? [] : [null!], nullAsset ? [null!] : []));
        var loaded = await Service.GetAsync(d.Household, saved.Id);
        Assert.Equal("Baseline", loaded.Name);
        Assert.Single(loaded.Groups);
        Assert.Single(loaded.FinancedAssets);
    }

    [Fact]
    public async Task NativeLoanPaymentsDoNotChangeWhenThePresetCurrencyChanges()
    {
        var d = await Setup();
        var assets = new ProjectionFinancedAssetSettings[] { new(d.Asset, 20_000, 9, 2), new(d.SecondAsset, 1000, 5, 1) };
        var saved = await Service.CreateAsync(d.Household, Settings(), [], assets);
        await Service.UpdateAsync(d.Household, saved.Id, Settings() with { CurrencyCode = "ZAR" }, [], assets);
        var loaded = await Service.GetAsync(d.Household, saved.Id);
        Assert.Equal("ZAR", loaded.CurrencyCode);
        Assert.Equal(20_000, loaded.FinancedAssets.Single(a => a.FinancedAssetId == d.Asset).MonthlyLoanPaymentInAssetCurrency);
        Assert.Equal(1000, loaded.FinancedAssets.Single(a => a.FinancedAssetId == d.SecondAsset).MonthlyLoanPaymentInAssetCurrency);
        await using var context = _database.CreateDbContext();
        Assert.Equal("ZAR", (await context.FinancedAssets.SingleAsync(a => a.Id == d.Asset)).CurrencyCode);
        Assert.Equal("GBP", (await context.FinancedAssets.SingleAsync(a => a.Id == d.SecondAsset)).CurrencyCode);
    }

    [Fact]
    public async Task DeferredChildWriteFailureRollsBackParentAndAllSelections()
    {
        var d = await Setup();
        var saved = await Service.CreateAsync(d.Household, Settings(), [new(d.Group, ProjectionBucket.Investment)],
            [new(d.Asset, 1000, 9, 2)]);
        var before = await Service.GetAsync(d.Household, saved.Id);
        await using var context = _database.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION "FailPresetChildWrite"() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM "ProjectionPresets"
                    WHERE "Id" = NEW."ProjectionPresetId" AND "Name" = 'Changed')
                    OR NOT EXISTS (SELECT 1 FROM "ProjectionPresetGroups"
                    WHERE "ProjectionPresetId" = NEW."ProjectionPresetId" AND "Bucket" = 2)
                    OR (SELECT count(*) FROM "ProjectionPresetFinancedAssets"
                    WHERE "ProjectionPresetId" = NEW."ProjectionPresetId") <> 2 THEN
                    RAISE EXCEPTION 'Expected all preset writes before deferred failure';
                END IF;
                RAISE EXCEPTION 'Synthetic preset child failure after writes';
            END;
            $$;
            CREATE CONSTRAINT TRIGGER "FailPresetChildWrite"
            AFTER INSERT OR UPDATE ON "ProjectionPresetFinancedAssets"
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION "FailPresetChildWrite"();
            """);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => Service.UpdateAsync(d.Household, saved.Id,
            Settings("Changed"), [new(d.Group, ProjectionBucket.Savings)],
            [new(d.Asset, 2000, 8, 3), new(d.SecondAsset, 500, 5, 1)]));
        Assert.Equal("Synthetic preset child failure after writes", failure.MessageText);
        var loaded = await Service.GetAsync(d.Household, saved.Id);
        Assert.Equal("Baseline", loaded.Name);
        Assert.Equal(before.UpdatedAtUtc, loaded.UpdatedAtUtc);
        Assert.Equal(ProjectionBucket.Investment, Assert.Single(loaded.Groups).Bucket);
        var loan = Assert.Single(loaded.FinancedAssets);
        Assert.Equal(d.Asset, loan.FinancedAssetId);
        Assert.Equal(1000, loan.MonthlyLoanPaymentInAssetCurrency);
        Assert.Equal(9, loan.AnnualLoanInterestPercent);
        Assert.Equal(2, loan.AnnualAssetAppreciationPercent);
    }

    [Fact]
    public async Task NamesAreUniquePerHouseholdAndListsAreOrderedAndScoped()
    {
        var d = await Setup();
        var other = await new HouseholdService(_database).CreateAsync("Other", "Alex");
        await Service.CreateAsync(d.Household, Settings("Z"), [], []);
        await Service.CreateAsync(d.Household, Settings("A"), [], []);
        await Service.CreateAsync(other.Id, Settings("A"), [], []);
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.CreateAsync(d.Household, Settings(" A "), [], []));
        var list = await Service.ListAsync(d.Household);
        Assert.Equal(new[] { "A", "Z" }, list.Select(p => p.Name));
        await Assert.ThrowsAsync<BusinessRuleViolationException>(() => Service.UpdateAsync(d.Household, list[1].Id, Settings("A"), [], []));
    }

    [Fact]
    public async Task ConcurrentDuplicateNamesCannotCreateTwoPresets()
    {
        var household = await new HouseholdService(_database).CreateAsync("Household", "Alex");
        async Task<bool> TryCreate()
        {
            try { await Service.CreateAsync(household.Id, Settings(), [], []); return true; }
            catch (BusinessRuleViolationException) { return false; }
        }
        var results = await Task.WhenAll(TryCreate(), TryCreate());
        Assert.Single(results, success => success);
        Assert.Single(await Service.ListAsync(household.Id));
    }

    [Fact]
    public async Task ReferencesAndOperationsCannotCrossHouseholds()
    {
        var first = await Setup();
        var second = await Setup();
        var saved = await Service.CreateAsync(first.Household, Settings(), [], []);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.GetAsync(second.Household, saved.Id));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.UpdateAsync(second.Household, saved.Id, Settings(), [], []));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.DeleteAsync(second.Household, saved.Id));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(first.Household, Settings("Group"), [new(second.Group, ProjectionBucket.Savings)], []));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(first.Household, Settings("Asset"), [], [new(second.Asset, 1, 1, 1)]));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.ListAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Service.CreateAsync(Guid.NewGuid(), Settings(), [], []));
    }

    [Fact]
    public async Task DuplicateAndUndefinedSelectionsAndDebtGroupsAreRejected()
    {
        var d = await Setup();
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Household, Settings(),
            [new(d.Group, ProjectionBucket.Investment), new(d.Group, ProjectionBucket.Savings)], []));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Household, Settings(),
            [new(d.Group, (ProjectionBucket)99)], []));
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Household, Settings(), [],
            [new(d.Asset, 1, 1, 1), new(d.Asset, 2, 2, 2)]));
        var classification = new ClassificationService(_database);
        var category = await classification.CreateCategoryAsync(d.Household, "Debts", FinancialType.Debt);
        var group = await classification.CreateGroupAsync(d.Household, category.Id, "Loan");
        await Assert.ThrowsAsync<InvalidValueException>(() => Service.CreateAsync(d.Household, Settings(), [new(group.Id, ProjectionBucket.Investment)], []));
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Currency")]
    [InlineData("YearsLow")]
    [InlineData("YearsHigh")]
    [InlineData("InvestmentContribution")]
    [InlineData("SavingsContribution")]
    [InlineData("InvestmentReturn")]
    [InlineData("SavingsReturn")]
    [InlineData("Inflation")]
    [InlineData("Payment")]
    [InlineData("LoanInterest")]
    [InlineData("Appreciation")]
    public async Task InvalidAssumptionsAreNotPersisted(string field)
    {
        var d = await Setup();
        var settings = field switch
        {
            "Name" => Settings() with { Name = " " },
            "Currency" => Settings() with { CurrencyCode = "12!" },
            "YearsLow" => Settings() with { DurationYears = 0 },
            "YearsHigh" => Settings() with { DurationYears = 101 },
            "InvestmentContribution" => Settings() with { MonthlyInvestmentContribution = -1 },
            "SavingsContribution" => Settings() with { MonthlySavingsContribution = -1 },
            "InvestmentReturn" => Settings() with { AnnualInvestmentReturnPercent = -100 },
            "SavingsReturn" => Settings() with { AnnualSavingsInterestPercent = 101 },
            "Inflation" => Settings() with { AnnualInflationPercent = -100 },
            _ => Settings()
        };
        var asset = new ProjectionFinancedAssetSettings(d.Asset,
            field == "Payment" ? -1 : 0, field == "LoanInterest" ? -1 : 0, field == "Appreciation" ? -100 : 0);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.CreateAsync(d.Household, settings, [], [asset]));
        Assert.Empty(await Service.ListAsync(d.Household));
    }

    [Fact]
    public async Task DeleteCascadesOnlyPresetChildren()
    {
        var d = await Setup();
        var saved = await Service.CreateAsync(d.Household, Settings(), [new(d.Group, ProjectionBucket.Savings)], [new(d.Asset, 1, 1, 1)]);
        await Service.DeleteAsync(d.Household, saved.Id);
        await using var context = _database.CreateDbContext();
        Assert.Empty(await context.ProjectionPresets.ToArrayAsync());
        Assert.Empty(await context.ProjectionPresetGroups.ToArrayAsync());
        Assert.Empty(await context.ProjectionPresetFinancedAssets.ToArrayAsync());
        Assert.True(await context.ItemGroups.AnyAsync(g => g.Id == d.Group));
        Assert.True(await context.FinancedAssets.AnyAsync(a => a.Id == d.Asset));
    }

    [Fact]
    public async Task DatabaseRejectsCrossHouseholdChildrenAndDuplicateGroups()
    {
        var first = await Setup();
        var second = await Setup();
        var saved = await Service.CreateAsync(first.Household, Settings(), [new(first.Group, ProjectionBucket.Savings)], []);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProjectionPresetGroups" ("ProjectionPresetId", "HouseholdId", "GroupId", "Bucket")
            VALUES ({saved.Id}, {first.Household}, {second.Group}, {1})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProjectionPresetGroups" ("ProjectionPresetId", "HouseholdId", "GroupId", "Bucket")
            VALUES ({saved.Id}, {second.Household}, {second.Group}, {1})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProjectionPresetGroups" ("ProjectionPresetId", "HouseholdId", "GroupId", "Bucket")
            VALUES ({saved.Id}, {first.Household}, {first.Group}, {1})
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ProjectionPresetFinancedAssets" ("ProjectionPresetId", "HouseholdId", "FinancedAssetId",
                "MonthlyLoanPaymentInAssetCurrency", "AnnualLoanInterestPercent", "AnnualAssetAppreciationPercent")
            VALUES ({saved.Id}, {first.Household}, {second.Asset}, {1}, {1}, {1})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
    }

    [Theory]
    [InlineData("DurationYears")]
    [InlineData("AnnualInflationPercent")]
    [InlineData("MonthlyInvestmentContribution")]
    public async Task DatabaseEnforcesAssumptionBounds(string column)
    {
        var d = await Setup();
        var saved = await Service.CreateAsync(d.Household, Settings(), [], []);
        await using var context = _database.CreateDbContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => column switch
        {
            "DurationYears" => context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ProjectionPresets" SET "DurationYears" = 0 WHERE "Id" = {saved.Id}
                """),
            "AnnualInflationPercent" => context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ProjectionPresets" SET "AnnualInflationPercent" = 'NaN'::numeric WHERE "Id" = {saved.Id}
                """),
            _ => context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ProjectionPresets" SET "MonthlyInvestmentContribution" = 'Infinity'::numeric WHERE "Id" = {saved.Id}
                """)
        });
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }
}
