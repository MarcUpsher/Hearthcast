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
  backend/
    src/
      Hearthcast.Api/
        Program.cs
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
        Validation/
          AllocationShareRules.cs
          RequiredAlphabeticCode.cs
          RequiredText.cs
        Persistence/
          HearthcastDbContext.cs
          Configurations/
            CategoryConfiguration.cs
            HouseholdConfiguration.cs
            ItemGroupConfiguration.cs
            PersonConfiguration.cs
            TrackedItemConfiguration.cs
            TrackedItemHouseholdAllocationConfiguration.cs
          DesignTime/
            HearthcastDbContextFactory.cs
          Migrations/
            <timestamp>_CreateHouseholdsAndPeople.cs
            <timestamp>_CreateHouseholdsAndPeople.Designer.cs
            <timestamp>_CreateCategoriesAndItemGroups.cs
            <timestamp>_CreateCategoriesAndItemGroups.Designer.cs
            <timestamp>_CreateTrackedItemsAndHouseholdAllocations.cs
            <timestamp>_CreateTrackedItemsAndHouseholdAllocations.Designer.cs
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
        Infrastructure/
          SqliteTestDatabase.cs
        Hearthcast.Data.Tests.csproj
  docs/
    schema-proposal.md
    relational-schema-plan.md
    household-person-slice.md
    classification-slice.md
    tracked-items-slice.md
    project-layout.md
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

Run solution build/test and local-tool restore from the repository root:

```sh
dotnet restore Hearthcast.slnx
dotnet tool restore
dotnet build Hearthcast.slnx --no-restore
dotnet test Hearthcast.slnx --no-build --no-restore
dotnet run --project backend/src/Hearthcast.Api
```

Migration commands target the data project and use the API as startup project:

```sh
dotnet ef migrations script --project backend/src/Hearthcast.Data --startup-project backend/src/Hearthcast.Api
```

When adding a subsequent migration, keep its output in `Persistence/Migrations`:

```sh
dotnet ef migrations add <DescriptiveName> --project backend/src/Hearthcast.Data --startup-project backend/src/Hearthcast.Api --output-dir Persistence/Migrations
```

The folder/namespace reorganisation does not change table names, migration IDs or
financial rules. The API remains a health endpoint; there is no application
database migration or seed operation on startup.
