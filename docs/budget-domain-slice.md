# Budget domain and persistence

17 November 2026. Implements the budget entities, EF mappings and PostgreSQL
migration from the [spending/budget plan](spending-and-budget-schema-plan.md).
[Budget setup services](budget-setup-service-slice.md) are now implemented.
[Snapshot services](budget-snapshot-service-slice.md) are also implemented.
[Budget setup HTTP endpoints](budget-setup-api-slice.md) are also implemented;
snapshot HTTP endpoints and UI remain subsequent work. Existing spending endpoints
are unchanged.

## Domain boundary

`Hearthcast.Data/Budgets` owns BudgetItem, BudgetSnapshot and BudgetEntry, with
BudgetFrequency (Week/Month/Year) and BudgetValueOrigin
(Unrecorded/Confirmed/CarriedForward). It shares expense categories, merchants,
code/text normalisation and the supported currency policy with Spending.

BudgetItem stores reusable defaults: household-unique normalised code, name,
category, optional merchant, currency, recurrence, notes and retirement. There is
no amount on the setup item. Its code is stable; descriptive defaults and terms
may change without changing saved plans. Recurrence means the full amount per
positive interval, such as every three months; no due dates or proration are implied.

BudgetSnapshot is a complete replacement effective from a date, with a supplied
client UUID and canonical creation hash, timestamps and one parent revision. A
household has at most one plan on each effective date. Effective-until is derived
from the next plan, not stored. As-of selection uses the latest date on or before
the requested date; before the first plan there is no known budget. Future and
backdated plans are valid. An empty plan is valid and complete. Adding a setup
item never creates an entry in an existing plan.

BudgetEntry saves its own item code/name, category/name, optional merchant/name,
currency, recurrence and notes. Internal metadata inputs support resolved current
defaults, saved source context and deliberate historical overrides. Services must
resolve all referenced identities in household scope. Retired setup can still be
used deliberately for historical capture. Current setup names/defaults are not
read when carrying saved context.

PlannedAmount is null for unrecorded, zero for an explicit zero, or a nonnegative
decimal below one trillion with at most six decimal places. Planned amounts may
have finer precision than actual payments; receipt minor-unit rules do not apply
to planned amounts. New currency selections use the existing supported policy.
Carrying a resolved saved entry, or correcting an existing entry while retaining
its item and currency, preserves that currency even if it has since left the
capture list. Codes must still normalise to three ASCII letters. No input flag
allows arbitrary unsupported currencies on new setup/entries; changing to another
code requires current policy validation. No conversion is inferred.
An included null entry makes IsComplete false; omitted items do not. Amounts can
use different currencies and recurrences within a plan. This slice provides no
single aggregate money total that would add incompatible values.

Budget dates and confirmation dates exclude DateOnly.MinValue/MaxValue because
Npgsql maps those sentinels to PostgreSQL infinities. Finite dates from 0001-01-02
through 9999-12-30 are accepted. Confirmation dates describe actual user checking
and are supplied explicitly, independently of effective dates or creation time.

## Carry, correction and provenance

An explicit nonnull amount requires a confirmation date and becomes Confirmed.
An unrecorded entry has null amount, confirmation date and carry-source link.
Carrying requires a resolved entry in a strictly earlier plan in the same
household, with the same item, currency, frequency and interval. The domain copies
the amount and original confirmation date. A nonnull copy becomes CarriedForward
with an informational source-entry link; copying null stays Unrecorded. Carry
inputs cannot simultaneously supply a new amount/confirmation. Changing currency
or recurrence cannot silently reuse the source number.

The optional snapshot template link is independent of individual carry links.
Selected entries may be carried, entered/confirmed, or omitted; an empty plan can
still record its initial template. Both template and carry links must point
strictly backwards, preventing cycles when created. Later source amount edits do
not propagate or invalidate a compatible copied amount.

Internal aggregate operations cover header/date correction, amount correction,
metadata-only correction, explicit entry metadata/value correction, addition/removal
and source detachment. CorrectEntryMetadata preserves the existing amount,
LastConfirmedDate, ValueOrigin and carry-source link without loading the source or
reconfirming the value. It permits descriptive/category/merchant/notes corrections
only while item identity, currency and recurrence remain the same. The operation
works for both linked and detached carried entries, even after the source amount
changes; it also preserves Confirmed and Unrecorded states. Invalid metadata or
changes to the amount's meaning are rejected before mutation. Use CorrectEntry
with an explicit value/confirmation (or deliberate source selection) for those
meaning changes; services still handle incompatible incoming-link detachment.
An explicit amount entry, including confirming an unchanged carried number,
records its supplied confirmation date and clears the carry link. Clearing the
amount makes it Unrecorded. Invalid edits are validated before changing the saved
aggregate. Every mutation checks the expected parent revision and advances it when
changing state; EF uses that revision as a concurrency token. Entry corrections preserve the
entry ID, while snapshot ID, creation time and creation hash remain unchanged.
No snapshot deletion operation is provided.

Detaching a link preserves the amount, reporting metadata, confirmation date and
ValueOrigin. A detached carried entry remains CarriedForward. Date corrections
validate any retained outgoing source links against resolved source aggregates.
Services must load complete aggregates, find incoming links affected by source
removal/identity/terms/date corrections, detach incompatible links and save all
affected parent revisions atomically. Those workflows are **not implemented as
services in this domain slice**; the subsequent [snapshot services](budget-snapshot-service-slice.md)
provide that orchestration. The persistence tests exercise the transaction primitives
and prove rollback on a concurrent descendant edit. Source amount changes alone
do not require detachment. A date correction may also change the effective
interval; the future API/UI must make that impact explicit.

## Persistence

Migration `20261117190213_AddBudgetPlans` adds BudgetItems, BudgetSnapshots and
BudgetEntries without changing spending tables. DbContext exposes all three sets.
Checks enforce normalised keys/text, currency shape, recurrence, finite dates,
amount bounds/precision, merchant identity/name pairing and valid provenance null
combinations. Self-source links, empty row/revision IDs and invalid creation hashes
are rejected. Numeric columns are unconstrained-scale PostgreSQL numeric with
explicit checks, so excess fractional precision is rejected rather than rounded.

Unique indexes enforce household/code, household/effective date and
snapshot/item. Composite foreign keys enforce household scope for all setup,
parent, carry-source and template references. All use Restrict; source detachment
clears only the nullable link, never HouseholdId. Snapshots have a direct household
FK so empty plans remain scoped. Saved setup identities cannot be deleted while
referenced; retire them instead.

Strict source ordering, compatibility and copied-value semantics require loaded
domain aggregates and transactional service validation; SQL checks deliberately
do not pretend to enforce cross-row dates or automatically update descendants.
Create hashes are stored but canonical request hashing and retry resolution
belong to the next service slice. Applying migrations to the application database
remains the existing separate deployment step.

## Verification

Domain and PostgreSQL tests cover saved metadata independence, deliberate retired
and historical capture, empty/omitted/null/zero semantics, mixed currencies,
precision, recurrence, explicit confirmations, compatible carry sources, source
detachment, revision conflicts, effective-date selection, household isolation,
direct-SQL constraint enforcement and migration rollback/reapplication preserving
existing spending. At the initial slice checkpoint, all 86 new budget tests passed
(32 domain, 54 PostgreSQL persistence). The budget-filtered run passed 88 tests,
including two existing projection tests whose names also match the filter.
The initial full backend regression run passed 1,302 tests (864 data/domain,
438 API), with no failures or skipped tests, including migration/model checks. The existing
NU1900 warning remains for unavailable NuGet vulnerability metadata; no packages
were added. Whitespace checks pass for tracked and new files. Only disposable test
databases were migrated; the application database was not changed.

Review follow-up adds nine domain and four persistence cases for metadata-only
corrections, linked/detached carry independence after source correction, state
preservation, invalid/stale edits, explicit meaning changes and historical currency
retention versus unsupported new selections. All 101 budget-filtered tests pass
(99 budget cases and two existing projection cases). Full follow-up verification
passes all 1,315 backend tests (877 data/domain, 438 API), with no failures or
skipped tests. These changes require no additional migration.

The subsequent [snapshot services](budget-snapshot-service-slice.md) implement
canonical create retries, transactional carry/correction/detachment, as-of reads
and current outgoing amounts. [Setup HTTP endpoints](budget-setup-api-slice.md) are
also implemented; snapshot HTTP endpoints remain next.
