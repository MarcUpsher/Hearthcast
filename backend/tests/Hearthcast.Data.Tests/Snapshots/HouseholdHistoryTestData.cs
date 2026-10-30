using Hearthcast.Data.Classification;
using Hearthcast.Data.Households;
using Hearthcast.Data.Snapshots;
using Hearthcast.Data.Tests.Infrastructure;
using Hearthcast.Data.TrackedItems;

namespace Hearthcast.Data.Tests.Snapshots;

internal sealed class HouseholdHistoryTestData
{
    public Guid HouseholdId { get; private init; }
    public Guid AdultAId { get; private init; }
    public Guid AdultBId { get; private init; }
    public Guid ChildId { get; private init; }
    public Guid InvestmentsCategoryId { get; private init; }
    public Guid TfsaGroupId { get; private init; }
    public Guid IsaGroupId { get; private init; }
    public Guid RetirementId { get; private init; }
    public Guid PensionId { get; private init; }
    public Guid EtfAlphaId { get; private init; }
    public Guid EtfBetaId { get; private set; }
    public Guid IsaId { get; private set; }
    public Guid EmergencyId { get; private set; }
    public Guid DepositId { get; private set; }
    public Guid ChildSavingsId { get; private set; }
    public Guid PersonalReserveId { get; private set; }
    public Guid LoanId { get; private set; }
    private Guid EmergencyGroupId { get; init; }
    private Guid DepositGroupId { get; init; }
    private Guid GeneralGroupId { get; init; }
    private Guid LoansGroupId { get; init; }

    public static async Task<HouseholdHistoryTestData> CreateAsync(PostgresTestDatabase database)
    {
        var households = new HouseholdService(database);
        var household = await households.CreateAsync("Cedar", "Adult A");
        var adultA = household.People.Single().Id;
        household = await households.AddPersonAsync(household.Id, "Adult B", true);
        var adultB = household.People.Single(person => person.DisplayName == "Adult B").Id;
        household = await households.AddPersonAsync(household.Id, "Child C", false);
        var classification = new ClassificationService(database);
        var investments = await classification.CreateCategoryAsync(household.Id, "Investments", FinancialType.Asset);
        var savings = await classification.CreateCategoryAsync(household.Id, "Savings", FinancialType.Asset);
        var borrowing = await classification.CreateCategoryAsync(household.Id, "Borrowing", FinancialType.Debt);
        var retirement = await classification.CreateGroupAsync(household.Id, investments.Id, "Retirement");
        var pension = await classification.CreateGroupAsync(household.Id, investments.Id, "Pension");
        var tfsa = await classification.CreateGroupAsync(household.Id, investments.Id, "TFSA");
        var isa = await classification.CreateGroupAsync(household.Id, investments.Id, "ISA");
        var emergency = await classification.CreateGroupAsync(household.Id, savings.Id, "Emergency");
        var deposit = await classification.CreateGroupAsync(household.Id, savings.Id, "Deposit");
        var general = await classification.CreateGroupAsync(household.Id, savings.Id, "General");
        var loans = await classification.CreateGroupAsync(household.Id, borrowing.Id, "Loans");
        var items = new TrackedItemService(database);
        return new HouseholdHistoryTestData
        {
            HouseholdId = household.Id, AdultAId = adultA, AdultBId = adultB,
            ChildId = household.People.Single(person => person.DisplayName == "Child C").Id,
            InvestmentsCategoryId = investments.Id, TfsaGroupId = tfsa.Id, IsaGroupId = isa.Id,
            EmergencyGroupId = emergency.Id, DepositGroupId = deposit.Id,
            GeneralGroupId = general.Id, LoansGroupId = loans.Id,
            RetirementId = (await items.CreateAsync(household.Id, retirement.Id, adultA,
                "Retirement fund A", "ZAR", "ZA", true)).Id,
            PensionId = (await items.CreateAsync(household.Id, pension.Id, adultB,
                "Pension B", "ZAR", "ZA", true)).Id,
            EtfAlphaId = (await items.CreateAsync(household.Id, tfsa.Id, adultA,
                "ETF Alpha", "ZAR", "ZA", true)).Id
        };
    }

    public async Task AddUkItemsAsync(PostgresTestDatabase database)
    {
        var items = new TrackedItemService(database);
        IsaId = (await items.CreateAsync(HouseholdId, IsaGroupId, AdultAId, "ISA A", "GBP", "GB", true)).Id;
        EmergencyId = (await items.CreateAsync(HouseholdId, EmergencyGroupId, AdultBId, "Emergency savings", "GBP", "GB", true,
            [new(AdultAId, 5_000), new(AdultBId, 5_000)])).Id;
        DepositId = (await items.CreateAsync(HouseholdId, DepositGroupId, AdultBId, "Deposit savings", "GBP", "GB", true)).Id;
        ChildSavingsId = (await items.CreateAsync(HouseholdId, GeneralGroupId, ChildId, "Child savings", "GBP", "GB", false)).Id;
        PersonalReserveId = (await items.CreateAsync(HouseholdId, GeneralGroupId, AdultAId, "Personal reserve", "GBP", "GB", false)).Id;
        LoanId = (await items.CreateAsync(HouseholdId, LoansGroupId, AdultAId, "Unsecured loan", "GBP", "GB", true)).Id;
    }

    public async Task ReplaceEtfAsync(PostgresTestDatabase database)
    {
        var items = new TrackedItemService(database);
        await items.SetRetiredAsync(HouseholdId, EtfAlphaId, true);
        EtfBetaId = (await items.CreateAsync(HouseholdId, TfsaGroupId, AdultAId, "ETF Beta", "ZAR", "ZA", true)).Id;
    }

    public ItemSnapshotValue[] UkValues() =>
    [
        new(IsaId, 1_000m), new(EmergencyId, 2_000m), new(DepositId, 0m),
        new(ChildSavingsId, 300m), new(PersonalReserveId, 400m), new(LoanId, 1_500m)
    ];

    public ItemSnapshotValue[] ReferenceValues() =>
    [
        new(RetirementId, 100_000m), new(PensionId, 80_000m), new(EtfBetaId, 25_000m), .. UkValues()
    ];
}
