# Hearthcast — schema proposal

Discussion draft, updated 30 October 2026. Proposed data model for Hearthcast.
The Household/Person, Classification, TrackedItem and initial ordinary-snapshot
capture/carry-forward/correction and financed-asset setup/snapshot persistence slices are in place. The remaining
entity definitions will be implemented in small, reviewed steps.
This document describes the model; see [Household/Person](household-person-slice.md),
[Classification](classification-slice.md), [Tracked items](tracked-items-slice.md),
[Ordinary snapshots](ordinary-snapshots-slice.md) and [Snapshot lifecycle](snapshot-lifecycle-slice.md),
[Financed-asset setup](financed-asset-setup-slice.md),
and [Financed-asset snapshots](financed-asset-snapshots-slice.md) for the currently implemented subset.
The [full snapshot summary](snapshot-summary-slice.md) now calculates all-recorded
totals on demand without adding tables. [Individual holdings](individual-holdings-summary-slice.md)
use saved holders and ownership shares. [Household summaries](household-summary-slice.md)
use saved inclusion and allocation shares; all three views are calculated on demand.
The [household/people API](household-api-slice.md) exposes the first setup endpoints;
other finance endpoints remain later slices.

## Model principles

- A household contains at least one person. At least one person participates in
  household finances. A person may have no tracked items.
- Categories, groups and tracked items are defined within the household, with
  holders assigned to items. Categories and groups are shared across people.
- Category names are user-defined. Each category has the system type Asset or
  Debt. Each group belongs to one category; each item belongs to one group.
- Each item has its actual holder recorded. Ordinary funds, pensions, accounts
  and cash pots have one named holder at 100%.
- Actual holders and household allocations are independent. A shared asset
  can have multiple holders with explicit ownership shares. A household allocation
  does not assert legal ownership or borrowing responsibility.
- Household inclusion is a boolean. Included items have household allocations
  totalling 100%, to people participating in household finances. An individual
  item defaults to its holder at 100%; sharing is an explicit change.
- A household allocation of 50% to each person still counts the full item once
  in the household total. A 100% allocation to its holder also counts the full
  item once. Excluded items remain visible in the holder's individual view.
- Snapshot entries record full values and copy relevant metadata. Later setup
  edits do not rewrite saved snapshots. No item versioning is required.
- Assets with borrowing use a dedicated FinancedAsset model, with
  FinancedAssetSnapshot entries under the same main snapshot as ordinary
  tracked-item entries. Property is a user-defined category, not a record type.
- Balances are nonnegative. Category type determines whether an amount adds to
  assets or is subtracted as debt. Overdraft handling is outside initial scope.
- Summaries are calculated from snapshots. Stored summary tables, currency
  conversion, graphs and projections are deferred.

## Current setup

| Table | Fields | Rules |
| --- | --- | --- |
| Household | Id, Name | Root for people, classification, items and snapshots. |
| Person | Id, HouseholdId, DisplayName, ParticipatesInHouseholdFinances | Membership does not require owning any items. |
| Category | Id, HouseholdId, Name, FinancialType | FinancialType is Asset or Debt. |
| ItemGroup | Id, HouseholdId, CategoryId, Name | User-defined grouping; inherits financial type. The composite category FK enforces the household. |
| TrackedItem | Id, HouseholdId, GroupId, HolderPersonId, Name, CurrencyCode, CountryCode, IncludeInHousehold, IsRetired | Persistent identity and current entry settings. Required single holder. |
| TrackedItemHouseholdAllocation | HouseholdId, TrackedItemId, PersonId, ShareBasisPoints | Required only for included items; totals 10,000 basis points (100%). |

An ordinary item's actual holder is recorded directly through HolderPersonId.
There is no ordinary holder-share table: that person's held view receives the
full item. Shared financed-asset ownership remains separate. ItemSnapshot copies
the single holder reference through HolderSnapshotPersonId.

Household allocation is similarly defaulted to 100% for an eligible individual
holder. If a nonparticipating holder's item is included, allocation to an eligible
household participant must be explicitly chosen.

Every holder and allocation recipient must belong to the item's household.
Excluded items have no household allocation rows. A participating person cannot
be switched off while receiving current household allocations, and the last
participant cannot be removed. Changes are validated together in a transaction.

Provider/account description and notes may be optional named fields. There is
no compulsory bank-account, instrument or holdings hierarchy: a user can record
a fund, pension, cash pot or other balance directly as a tracked item.

## Saved snapshots

| Table | Fields | Rules |
| --- | --- | --- |
| Snapshot | Id, HouseholdId, SnapshotDate, DatePrecision, HouseholdName, CreatedAtUtc, UpdatedAtUtc, Revision | One dated capture. HouseholdName is copied. |
| SnapshotPerson | Id, HouseholdId, SnapshotId, PersonId, DisplayName, ParticipatesInHouseholdFinances | Copied household roster, including people without items. |
| ItemSnapshot | Id, HouseholdId, SnapshotId, TrackedItemId, HolderSnapshotPersonId, ItemName, CategoryId, CategoryName, FinancialType, GroupId, GroupName, CurrencyCode, CountryCode, IncludeInHousehold, Amount, ValueOrigin, LastConfirmedDate, LastConfirmedPrecision, CarriedFromItemSnapshotId | One full value and copied reporting context per tracked item. |
| ItemSnapshotHouseholdAllocation | SnapshotId, ItemSnapshotId, SnapshotPersonId, ShareBasisPoints | Copied household attribution for included entries. |

Naming defines responsibilities: Snapshot is the shared dated capture; ItemSnapshot
records one TrackedItem at that capture; FinancedAssetSnapshot records one
FinancedAsset at that capture. Both child types reference the same SnapshotId and
their respective persistent item IDs. The user creates and saves the entire
capture as one operation, with header and child changes committed atomically.

Stable source IDs connect history to the same people, classifications and items.
Saved names, types, inclusion settings and shares drive historical reports.
Relationships between snapshot entries and snapshot people must stay within the
same snapshot. Current names and settings must not substitute for saved copies.

### Dates

- Ordinary capture defaults to today's actual date with Day precision.
- When only a month is known, store the first of that month and Month precision.
  For example, May 2023 is stored as 2023-05-01 / Month, displayed as May 2023.
- The normalised first-of-month date does not claim an exact valuation day.
- Creation/edit timestamps describe data entry, not the balance date.
- Multiple snapshots per month are possible. Monthly selection rules can be
  agreed when seeing the first summaries; there is no monthly uniqueness constraint.

### Values and carrying forward

Fresh capture, carry-forward and value corrections are implemented. See the
[snapshot lifecycle guide](snapshot-lifecycle-slice.md) and
[household history specification](household-history-tests.md) for the exact source
selection, clear-versus-carry and stale-edit rules.

- `Amount` is nullable and otherwise nonnegative. Null means unrecorded; zero is
  a known balance. Missing values make affected totals visibly incomplete.
- `ValueOrigin` is Unrecorded, Confirmed or CarriedForward. Leaving a prefilled
  value untouched keeps it carried; explicitly checking an unchanged value can
  confirm it without changing the number.
- A carried balance keeps its previous confirmation date and precision. Unknown
  historical confirmation dates may remain null; import time is not confirmation.
- `CarriedFromItemSnapshotId` optionally identifies the previous ItemSnapshot for the same
  tracked item. The amount itself is copied and independently saved.
- There is at most one entry for `(SnapshotId, TrackedItemId)`.
- New ordinary snapshots list active items and copy their current setup. Previous
  values can be carried forward; they do not bring obsolete household settings
  into the new snapshot.
- Retired items stay in older snapshots and stop appearing in new ordinary
  checklists. A replacement fund or account gets a new tracked-item identity.
- A saved snapshot can be corrected deliberately, using its expected Revision.
  A stale edit is rejected. Corrections confirm or clear saved entries without
  changing their copied reporting context or later saved copies of their amounts.
- Retain referenced identities and snapshots; avoid cascade deletion of history.

### Backdated captures

Backdated data is not expected to be a major manual workflow. Copied metadata is
sufficient provided the entry records the intended historical settings.

The planned entry/import process can supply historical metadata or use an existing
snapshot as a starting point and edit it. Current setup is a convenience default,
not evidence that the same items, holders or splits existed in the past. There is
no requirement for a separate timeline/versioning engine.

## Financed assets

Agreed: a dedicated FinancedAsset entity combines an asset and its borrowing
setup. FinancedAssetSnapshot records the changing balances within the common
dated snapshot. Neither the asset nor its loan needs a duplicate TrackedItem.

Record type and user classification are independent: a house could use category
Property / group Residential, while a car could use Vehicles / Cars. The financed
asset belongs to a user-defined group under an Asset category. It retains its
identity and history after the loan balance reaches zero.

The table below describes logical fields and associations. Owner, loan-holder
and household-allocation associations will be represented by related person rows,
with copied equivalents for saved financed-asset snapshots, in the final schema review.

| Table | Fields | Rules |
| --- | --- | --- |
| FinancedAsset | Id, HouseholdId, GroupId, Name, CurrencyCode, CountryCode, PurchaseDate, PurchasePrice, OriginalLoanAmount, owners and shares, loan holder(s), IncludeInHousehold, household allocation, IsRetired | Current setup; purchase price and original borrowing are distinct. |
| FinancedAssetSnapshot | Id, SnapshotId, FinancedAssetId, CurrentValuation, OutstandingLoanBalance, AvailableToWithdraw, confirmation/carry-forward information for each balance, copied asset metadata and person associations | One financed-asset entry per main snapshot, independent of ordinary ItemSnapshot rows. |

- Shared assets have their actual owners and explicit shares recorded.
- Record the loan's named holder(s) separately from asset ownership and
  household allocation. The repayment account does not determine the reporting split.
- The three changing inputs are current valuation, outstanding loan balance and
  optional amount available to withdraw. Each has independent confirmation/carry-forward metadata.
- AvailableToWithdraw may be labelled Access bond for a mortgage where that term is
  appropriate; it is not required for loans without this facility.
- Saved entries copy the original loan amount, category/group IDs and names,
  financial type, and other relevant setup, so changing current setup cannot
  rewrite historical reporting or derived figures.
- Relationships stay within the household and snapshot, with at most one entry
  for each `(SnapshotId, FinancedAssetId)` pair.
- Where overpayments have already reduced outstanding debt, the amount available to withdraw is
  informational. It does not become another asset or a second deduction.
- The calculation is a snapshot of balances, not a record of transactions or
  cumulative cash payments including interest. Do not clamp unexpected derived
  results to zero silently.

Calculated figures:

| Figure | Formula |
| --- | --- |
| Principal reduction excluding withdrawable funds, where applicable | Original loan amount - amount available to withdraw - outstanding loan balance |
| Total loan balance reduction | Original loan amount - outstanding loan balance |
| Current equity | Current valuation - outstanding loan balance |

Use the label Loan balance reduction rather than Total paid, because the
balances do not establish cumulative cash payments. Purchase price separately
allows asset-value change to be calculated; starting equity is purchase price
minus original borrowing when those figures describe the purchase financing.

Household reporting includes financed-asset equity once, applying the saved household
allocation for its person breakdown. A future gross asset/debt presentation can
show asset value and loan separately as the components of that same equity.
Do not add equity again alongside those components. Missing inputs make only the
dependent calculations incomplete; an unknown or inapplicable withdrawal balance does not prevent
calculating equity or total loan balance reduction when their inputs are known.
For a loan without a withdrawal facility, there is no need to calculate a separate
reduction excluding withdrawable funds. AvailableToWithdraw stays unknown when
applicable but unrecorded.

## Reporting and technical constraints

- Held view: the ordinary entry's full amount belongs to its saved named holder;
  financed assets have their separate copied owner shares.
- Household view: each included full amount once; saved household shares supply
  the person breakdown. These are alternative perspectives over the same money.
- Category/group reporting uses saved classification. Financed assets contribute
  their equity under their saved category/group. Net worth is ordinary tracked
  assets minus ordinary tracked debts plus included financed-asset equity.
  Different currencies remain separate until conversion rules are agreed.
- Use exact decimal arithmetic and exact persistence for money, not binary
  floating point. Ordinary amounts now use native numeric in PostgreSQL,
  without fixed-scale rounding; see the ordinary-snapshot implementation guide.
- Proposed share storage is integer basis points: 10,000 represents 100%.
  Avoid premature rounding of per-person money when aggregating totals.
- Holder and household shares each total 100% within their own applicable set.
  Row uniqueness, foreign keys and transactional validation enforce the model.
- Currency changes require a freshly supplied balance. Do not carry an old
  numeric amount into a new currency automatically.
- No automatic filling of absent historical months or mixing of independent
  source snapshots is required by this model.

## Synthetic reporting examples

| Item | Actual holder(s) | Household allocation | Household inclusion |
| --- | --- | --- | --- |
| Pension A | Person A 100% | Person A 100% | Yes |
| Pension B | Person B 100% | Person B 100% | Yes |
| ISA | Person A 100% | Person A 100%, or an explicitly chosen split | Yes |
| Emergency savings | Person B 100% | Person A 50%, Person B 50% | Yes |
| Child savings | Child 100% | None | No |
| Home (FinancedAsset, category Property) | Asset owners recorded with shares; loan holders recorded separately | Person A 50%, Person B 50% of equity | Yes |
| Car (FinancedAsset, category Vehicles) | Person A 100%; loan holder recorded separately | Person A 100% of equity | Yes |

Examples illustrate the model, not default assumptions about anyone's accounts.
The implementation guides identify the completed persistence slices; API/UI workflows
remain later steps.
