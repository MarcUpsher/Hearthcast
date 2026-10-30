# Project layout and naming

Hearthcast separates backend source, backend tests and the future frontend. Within
the data project, related feature code stays together and EF infrastructure lives
under Persistence. This is the repository's convention; .NET does not require a
single universal folder layout.

```text
hearthcast/
  .config/
    dotnet-tools.json
  Hearthcast.slnx
  global.json
  Dockerfile
  .dockerignore
  compose.yaml
  .env.example
  backend/
    src/
      Hearthcast.Api/
        Program.cs
        Health/
          DatabaseHealthCheck.cs
        Properties/
          launchSettings.json
        Hearthcast.Api.csproj
        appsettings.json
        appsettings.Development.json
      Hearthcast.Data/
        Classification/
          Category.cs
          ItemGroup.cs
          FinancialType.cs
          ClassificationService.cs
        Households/
          Household.cs
          Person.cs
          HouseholdService.cs
        TrackedItems/
          TrackedItem.cs
          TrackedItemHouseholdAllocation.cs
          TrackedItemAllocationShare.cs
          TrackedItemService.cs
        FinancedAssets/
          FinancedAsset.cs
          FinancedAssetOwner.cs
          FinancedAssetLoanHolder.cs
          FinancedAssetHouseholdAllocation.cs
          FinancedAssetShare.cs
          FinancedAssetService.cs
        Snapshots/
          FinancedAssetSnapshot.cs
          FinancedAssetSnapshotOwner.cs
          FinancedAssetSnapshotLoanHolder.cs
          FinancedAssetSnapshotHouseholdAllocation.cs
          FinancedAssetBalanceKind.cs
          FinancedAssetSnapshotValue.cs
          Snapshot.cs
          SnapshotPerson.cs
          ItemSnapshot.cs
          ItemSnapshotHouseholdAllocation.cs
          ItemSnapshotValue.cs
          DatePrecision.cs
          SnapshotValueOrigin.cs
          SnapshotService.cs
          SnapshotPeriod.cs
          SnapshotConflictException.cs
        Validation/
          AllocationShareRules.cs
          RequiredAlphabeticCode.cs
          RequiredText.cs
        Summaries/
          SnapshotSummaryService.cs
          SnapshotSummaryCalculator.cs
          Models/
            SnapshotSummary.cs
            SnapshotPersonSummary.cs
            SnapshotPersonShare.cs
            SnapshotBalanceSummary.cs
            SnapshotAmountSubtotal.cs
            SnapshotSummaryTotals.cs
            SnapshotCurrencySummary.cs
            SnapshotCategorySummary.cs
            SnapshotGroupSummary.cs
            ItemSnapshotSummary.cs
            FinancedAssetSnapshotSummary.cs
        Persistence/
          HearthcastDbContext.cs
          SerializableTransaction.cs
          Configurations/
            FinancedAssetSnapshotConfiguration.cs
            FinancedAssetSnapshotOwnerConfiguration.cs
            FinancedAssetSnapshotLoanHolderConfiguration.cs
            FinancedAssetSnapshotHouseholdAllocationConfiguration.cs
            FinancedAssetConfiguration.cs
            FinancedAssetOwnerConfiguration.cs
            FinancedAssetLoanHolderConfiguration.cs
            FinancedAssetHouseholdAllocationConfiguration.cs
            CategoryConfiguration.cs
            HouseholdConfiguration.cs
            ItemGroupConfiguration.cs
            PersonConfiguration.cs
            TrackedItemConfiguration.cs
            TrackedItemHouseholdAllocationConfiguration.cs
            SnapshotConfiguration.cs
            SnapshotPersonConfiguration.cs
            ItemSnapshotConfiguration.cs
            ItemSnapshotHouseholdAllocationConfiguration.cs
          DesignTime/
            HearthcastDbContextFactory.cs
          Migrations/
            <timestamp>_CreateInitialPostgresSchema.cs
            <timestamp>_CreateInitialPostgresSchema.Designer.cs
            <timestamp>_AddSnapshotCarryForwardAndCorrections.cs
            <timestamp>_AddSnapshotCarryForwardAndCorrections.Designer.cs
            <timestamp>_AddFinancedAssetSetup.cs
            <timestamp>_AddFinancedAssetSetup.Designer.cs
            <timestamp>_AddFinancedAssetSnapshots.cs
            <timestamp>_AddFinancedAssetSnapshots.Designer.cs
            HearthcastDbContextModelSnapshot.cs
        Hearthcast.Data.csproj
    tests/
      Hearthcast.Data.Tests/
        Classification/
          ClassificationPersistenceTests.cs
        Households/
          HouseholdPersistenceTests.cs
        TrackedItems/
          TrackedItemPersistenceTests.cs
          TrackedItemAllocationTests.cs
          TrackedItemTestSetup.cs
        Snapshots/
          FinancedAssetSnapshotTests.cs
          FinancedAssetSnapshotIntegrityTests.cs
          SnapshotPersistenceTests.cs
          SnapshotIntegrityTests.cs
          PostgresTransactionTests.cs
          HouseholdHistoryTestData.cs
          HouseholdHistoryTests.cs
          SnapshotCarryForwardTests.cs
          SnapshotCarryIntegrityTests.cs
          SnapshotCorrectionTests.cs
          SnapshotConcurrencyTests.cs
        FinancedAssets/
          FinancedAssetTestSetup.cs
          FinancedAssetPersistenceTests.cs
          FinancedAssetIntegrityTests.cs
        Summaries/
          SnapshotSummaryTests.cs
        Infrastructure/
          PostgresTestDatabase.cs
          InterceptingContextFactory.cs
          ConcurrentSaveBarrier.cs
        Hearthcast.Data.Tests.csproj
  docs/
    schema-proposal.md
    relational-schema-plan.md
    household-person-slice.md
    classification-slice.md
    tracked-items-slice.md
    ordinary-snapshots-slice.md
    snapshot-lifecycle-slice.md
    snapshot-summary-slice.md
    household-history-tests.md
    project-layout.md
    containers-and-postgres.md
```

The frontend will be created under `frontend/` in a later step. Build output and
temporary financial files are excluded by the root `.gitignore`.

## Conventions

- Projects and assemblies use `Hearthcast.<Responsibility>`; test projects append
  `.Tests` to the project being tested.
- Put each main C# type in a file with the same name, using PascalCase.
- Feature folders use descriptive names, such as `Households` and `Classification`. Household
  and Person belong together because membership is controlled by the household.
  Add later feature folders when those features are implemented.
- Namespaces follow project and folder names: `Hearthcast.Data.Households`,
  `Hearthcast.Data.Persistence.Configurations`, and so on.
- Entities use singular names. Services describe the feature they operate on.
  EF mappings use `<Entity>Configuration`; tests use `<Subject>Tests`.
- Shared argument-validation functions live under Validation. RequiredText.Trim
  validates and trims required text, preserving the caller's argument name.
  RequiredAlphabeticCode handles required uppercase letter codes. AllocationShareRules
  defines the basis-point range and the shared 100% value used by validation,
  default allocations and the database check. Household eligibility and allocation
  totals stay in their domain features.
  Entity methods such as Rename retain responsibility for changing entity state.
- Tests mirror the feature folders. Database integration tests identify their
  purpose explicitly, as in `HouseholdPersistenceTests`.
- Keep the DbContext, EF mappings, migration tooling and migrations under
  Persistence. Migrations retain EF-generated timestamps and descriptive names.
- Keep the existing three projects. Add another project only when it serves a
  clear dependency or deployment boundary.

The namespace convention follows Microsoft's [namespace naming guidance](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces).
The `.config/dotnet-tools.json` manifest is a supported [local-tool manifest location](https://learn.microsoft.com/en-us/dotnet/core/tools/local-tools-how-to-use).

## Commands

The container workflow and optional host-build commands are documented in
[containers and PostgreSQL](containers-and-postgres.md). Run them from the
repository root. Keep new migrations under Persistence/Migrations.

The API currently exposes liveness/readiness endpoints only. A separate migration
process applies schema changes; the ordinary API does not migrate or seed data.
