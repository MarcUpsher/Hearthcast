# Financed-asset setup

Implemented locally, 30 October 2026. This is the setup review point from the
[relational plan](relational-schema-plan.md). Balance snapshots and calculations
are the next review point.

## Files to review

| File | Responsibility |
| --- | --- |
| [FinancedAsset.cs](../backend/src/Hearthcast.Data/FinancedAssets/FinancedAsset.cs) | Acquisition details, classification, retirement and association rules. |
| [FinancedAssetOwner.cs](../backend/src/Hearthcast.Data/FinancedAssets/FinancedAssetOwner.cs) | Actual owner and ownership share. |
| [FinancedAssetLoanHolder.cs](../backend/src/Hearthcast.Data/FinancedAssets/FinancedAssetLoanHolder.cs) | Named loan holder, without debt percentages. |
| [FinancedAssetHouseholdAllocation.cs](../backend/src/Hearthcast.Data/FinancedAssets/FinancedAssetHouseholdAllocation.cs) | Household recipient and allocation share. |
| [FinancedAssetShare.cs](../backend/src/Hearthcast.Data/FinancedAssets/FinancedAssetShare.cs) | Explicit person/share input for ownership or household allocation. |
| [FinancedAssetService.cs](../backend/src/Hearthcast.Data/FinancedAssets/FinancedAssetService.cs) | Household-scoped create, reload, atomic association replacement and retirement. |
| [HouseholdService.cs](../backend/src/Hearthcast.Data/Households/HouseholdService.cs) | Extended participation and person-removal guards. |
| [FinancedAssetPersistenceTests.cs](../backend/tests/Hearthcast.Data.Tests/FinancedAssets/FinancedAssetPersistenceTests.cs) | Setup rules, round trips, updates, scope, rollback and migration preservation. |
| [FinancedAssetIntegrityTests.cs](../backend/tests/Hearthcast.Data.Tests/FinancedAssets/FinancedAssetIntegrityTests.cs) | Native constraints, deletion guards, failed replacement and controlled concurrency. |

Each of the four entities has its own mapping in Persistence/Configurations.
AddFinancedAssetSetup is an additive migration creating four tables; existing
migrations remain unchanged.

## Setup rules

A financed asset uses a group under an Asset category. Names, currency and country
use the same validation as ordinary items. Purchase date and purchase price may be
unknown independently. Original loan amount is required; both monetary inputs,
when present, are nonnegative, exact decimal/native PostgreSQL numeric values.
Purchase price and original borrowing need not be equal. Zero borrowing is valid.
HasWithdrawalFacility records whether a later AvailableToWithdraw balance applies.

Ownership is required and totals 10,000 basis points (100%), including for excluded
assets. Included household allocations separately total 10,000 and go only to
participating household members. Excluded assets have no household allocations.
Owners and named loan holders can be nonparticipants. Every person is within the
asset's household, and each person appears at most once in each association.

All associations are explicit inputs: household shares are not inferred from
ownership or loan holders. A 50/50 jointly owned home can have a single named loan
holder and a different household allocation. Loan holders carry no percentage.
An empty loan-holder list is allowed, including for a repaid loan with a retained
original borrowing amount. Setup has no current loan balance, so it cannot infer
whether borrowing is still outstanding from OriginalLoanAmount alone.

SetPersonAssociationsAsync replaces ownership, loan-holder links, inclusion and
household allocations together. It validates the whole requested change before
mutating associations, retains existing rows for unchanged identities, and removes
obsolete rows explicitly. There is no implicit default when a collection is empty.
Creation and association changes run in the existing serializable transaction and
retry helper. GetAsync loads the complete graph with one SQL statement.
Retirement/reactivation also loads that graph and returns the owners, loan holders,
household allocations, linked people, group and category along with the changed
retirement flag. Returned collections therefore describe the saved setup.

Association edits currently apply the caller's supplied replacement without an
expected revision token. Transactions preserve the rules, but detecting stale
setup forms remains a requirement for the future editing workflow.

Renaming and retirement preserve identity and links. Retirement alone does not
release allocations: reassign them before a recipient stops participating. Actual
ownership/loan-holder links prevent person removal, but alone do not prevent that
person from stopping participation. Existing ordinary-item and snapshot guards
remain in effect.

Composite foreign keys prevent cross-household groups, assets and people; all
references restrict deletion. PostgreSQL enforces individual share ranges,
duplicate links, required metadata, code formats and finite/nonnegative amounts.
Collection totals, participation eligibility and Asset-category selection are
transactional application rules. Direct SQL is not a supported setup workflow.

## Boundaries and verification

This slice creates no ordinary TrackedItem for the asset or mortgage. It adds no
FinancedAssetSnapshot, equity calculation, summaries or finance HTTP endpoints.
Existing ordinary snapshot capture remains unchanged and does not yet include
financed assets. Acquisition details, group, currency and facility flags currently
have creation-time inputs; further editing services can follow when needed.

Tests use synthetic data and isolated PostgreSQL databases. They cover shared and
individual ownership, independent household allocation, multiple/empty loan-holder
lists, exclusion, retirement, missing acquisition details, exact money, invalid
associations, foreign references, native constraints, migration upgrade/rollback,
atomic setup/replacement failure and forced allocation/participation races during
both creation and later editing. Migration downgrade removes financed-asset setup
tables and their rows; it is tested only against disposable data.

Verification: the build has zero warnings/errors. All 273 PostgreSQL integration
cases pass in one full run, including 72 financed-asset setup cases. Four regression
cases inspect the retirement/reactivation return value directly for included and
excluded assets. The setup migration test explicitly targets the pre-setup and
setup migration IDs, so later migrations cannot change its rollback boundary.

The test image is rebuilt independently. The application containers/database have
not been upgraded and contain no new setup or demo data. Review the local diff
before applying the migration to the application or committing/pushing.
