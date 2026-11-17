# Spending and budget schema — discussion draft

17 November 2026. Schema/workflow plan. The initial ExpenseCategory, Merchant,
SpendingItem and SpendingItemMerchantPrice domain/persistence slice is implemented;
see [spending setup](spending-setup-slice.md) for its exact boundary and technical
conventions. [Setup services](spending-setup-service-slice.md) and the
[setup API](spending-setup-api-slice.md) are also implemented.
[Spending-record domain/persistence](spending-records-slice.md) is implemented;
[record services](spending-record-service-slice.md) are also implemented.
[Record HTTP endpoints and bounded history](spending-record-api-slice.md) are
implemented. UI and budgets remain planned.
The user prefers dated budget plans that remain effective until replaced. Variable
outgoings likewise have an amount that applies until explicitly replaced; their
amounts belong to budget entries, not the reusable outgoing item.
Usual purchase prices are remembered per item/merchant/currency and changed only
by explicit user choice; last paid prices remain separate derived information.
Both quick category-and-amount capture and optional itemised capture are agreed;
broad spending trends are the primary goal, with product detail available when useful.
Revised against the [review handoff](spending-and-budget-schema-review.md), including
its follow-up review and the decisions clarified with the user there. The rules
below resolve its review recommendations as planning defaults; they are not
implemented behaviour.

## Purpose and boundaries

Understand and optimise everyday spending: capture known regular outgoings, learn
what food/eating out/etc. actually cost, and choose allowances informed by that
history. Users define categories, merchants, spending items and budget items.
Names do not imply behaviour, currency, frequency or required classifications.
The initial use is the user's UK life, but the schema has no UK-only boundary or
hard-coded GBP default. A food allowance is user-chosen, not system-prescribed.

Record actual purchases/payments, maintain dated spending plans, and eventually
compare actual spending against those plans. Reuse the existing pattern of stable
setup identities plus copied historical metadata, exact decimal money, household
scope, atomic saves and revision checks.

Keep these concepts separate:

- Existing financial `Snapshot`: asset/debt balances at a date.
- `SpendingRecord`: one actual purchase, payment or refund, with line items.
- `BudgetSnapshot`: a complete spending plan effective from a date.

Spending records do not carry purchases forward. Budget snapshots do carry planned
amounts forward. Neither automatically changes account balances or net worth.
These new records do not need an existing financial SnapshotId.

Use Purchase/Payment in the UI rather than Invoice: an unpaid invoice is not yet
cash spending. First-version reporting uses the date paid/purchased, not service
periods or accrual accounting. A utility direct debit can differ from consumption;
record the payment here and defer supplier credits/usage accounting.

Counting convention: record purchases once. A later credit-card repayment, own-
account transfer or cash withdrawal does not count the same money again. Record
cash purchases when made. This is a documented entry convention, not automatic
classification by category names or a transfer/accounting subsystem. If the user
records a loan/mortgage outgoing, record the full payment; do not split principal
and interest here. Existing financial snapshots handle balances/net worth.

Income, savings allocations, cash-flow forecasting, affordability calculations and
South African rental/property/tax accounting are outside this work. Mortgage
payments need no dedicated workflow. This is spending capture and user-configured
budgeting, not a complete accounting system.

## Proposed tables

All identities are UUIDs. Every table is household-scoped, directly or through an
enforced parent relationship. Setup names/codes below are current defaults;
historical records save their own copies.

| Table | Principal fields | Purpose |
| --- | --- | --- |
| ExpenseCategory | Id, HouseholdId, Name, IsRetired | Food, Clothing, Utilities, Housing, etc. Shared by spending and budgets. |
| Merchant | Id, HouseholdId, Name, IsRetired | Reusable shop/provider, such as Waitrose, Amazon or an internet provider. |
| SpendingItem | Id, HouseholdId, Code, Name, ExpenseCategoryId, UnitKey, UnitLabel, Notes?, IsRetired | Reusable eggs/milk/clothing or payment description. Usual prices live in SpendingItemMerchantPrice. |
| SpendingItemMerchantPrice | Id, HouseholdId, SpendingItemId, MerchantId, CurrencyCode, UnitKey, UnitLabel, UsualUnitPrice, SourceSpendingLineId?, UpdatedAtUtc, Revision | Explicitly remembered usual price for this item at this merchant in this currency and unit. |
| SpendingRecord | Id, HouseholdId, Kind, SpendingDate, MerchantId, MerchantName, CurrencyCode, CurrencyMinorUnits, ExternalReference?, Notes?, ReceiptTotal?, OriginalSpendingRecordId?, OriginalSpendingRecordKind?, CreatedAtUtc, UpdatedAtUtc, Revision, CreationRequestHash, VoidedAtUtc? | One purchase/payment or refund. Saved precision protects history; companion original kind enforces a Payment FK. Client-generated Id supports later service-level retries. |
| SpendingLine | Id, HouseholdId, SpendingRecordId, Position, CurrencyCode, CurrencyMinorUnits, SpendingItemId?, ItemCode?, ItemName?, Description?, ExpenseCategoryId, ExpenseCategoryName, UnitKey?, UnitLabel?, ReceiptDescription?, MerchantItemReference?, Quantity?, UnitPrice?, NetAmount, PromotionStatus, Notes? | Categorised amount, optionally linked to a catalogue item. A composite FK matches parent currency/precision. PromotionStatus is Unknown, Yes or No. |
| BudgetItem | Id, HouseholdId, Code, Name, ExpenseCategoryId, MerchantId?, CurrencyCode, Frequency, IntervalCount, Notes?, IsRetired | Reusable outgoing or allowance. No stored cost or required fixed/variable classification on the base item. |
| BudgetSnapshot | Id, HouseholdId, EffectiveFrom, Notes?, CreatedAtUtc, UpdatedAtUtc, Revision, CreationRequestHash, CopiedFromBudgetSnapshotId? | Complete plan, effective until the next plan begins. |
| BudgetEntry | Id, HouseholdId, BudgetSnapshotId, BudgetItemId, ItemCode, ItemName, ExpenseCategoryId, ExpenseCategoryName, MerchantId?, MerchantName?, PlannedAmount?, CurrencyCode, Frequency, IntervalCount, Notes?, ValueOrigin, LastConfirmedDate?, CarriedFromBudgetEntryId? | Planned amount and copied metadata for an outgoing/allowance at this plan date. |

Expense categories are separate from existing `Category`/`ItemGroup`, whose
financial type is specifically Asset or Debt. No changes to that classification
are needed. Initial scope is whole-household spending; person attribution and
shared-payment allocations can follow if required.

`BudgetItem` supports both named commitments and broad allowances. Food can have
one £500 monthly allowance while many different SpendingItems map to Food. Eggs
do not require their own budget. Avoid introducing separate outgoing-snapshot and
category-budget systems that would duplicate the same planned money.

Omit CostBehaviour initially. Fixed/variable and mandatory/discretionary are
different descriptive dimensions; neither controls entry or calculation behaviour.
Users can describe them in Notes; structured descriptive metadata can follow if
use demonstrates a need. `Frequency` initially supports Week, Month and Year with a positive
IntervalCount (e.g. every three months). PlannedAmount means the full amount per
such interval, not a unit price or a balance. Zero is an explicit zero budget;
null is unrecorded and makes the relevant plan total incomplete.

For a variable utility outgoing, PlannedAmount is the currently applicable outgoing
amount until a later plan changes it. Seasonal usage is the reason for an explicit
change, not a requirement to predict usage or derive a supplier balance. The first
version needs one amount per budget entry, not separate forecast and target fields.

### Latest outgoing amount on the item

Expose the currently effective outgoing amount alongside BudgetItem metadata in
the item list/detail response. This is derived from the item's entry in the active
BudgetSnapshot as of the requested date (today by default), not another editable
cost stored on BudgetItem. Include amount, currency, frequency, plan EffectiveFrom,
source snapshot ID and confirmation/carry-forward status. Label it Current outgoing
amount to distinguish it from the last actual payment recorded in spending.

For example: Electricity — £20/month, plan effective 15 February. Opening a new
plan can prefill from the selected source entry. Future-dated plans do not replace
today's displayed amount early. If there is no active plan or the active plan has
no entry/amount for this item, show Not recorded rather than falling back to a
superseded historical value. Retired items can retain a separately labelled
historical last-recorded amount if needed later.

## Purchase entry and item identity

### Quick, split and itemised capture — agreed behaviour

Quick capture asks for date, merchant, currency, category and amount, with optional
description/notes. Save one SpendingLine with its category and NetAmount; no
catalogue item, quantity or unit price is required. The UI does not need to expose
the underlying line editor to make this easy.

The same record may instead contain broad category splits or individual receipt
items. For example, Gail's £12.50 can be one Eating out line, two unlinked lines
for Coffee £7 and Food £5.50, or detailed product lines optionally linked to the
catalogue. Mixed detail within a record is supported. All saved records require
at least one categorised line, with a nonnegative NetAmount on each line.

The record total is always the sum of its line NetAmounts. ReceiptTotal remains
an optional reconciliation input, not a second counted amount. Replacing a quick
entry with itemised lines removes the original summary line in the same atomic
revision-checked save; never retain it as another counted line. The editor can
show an explicit categorised remainder when some of a receipt is still unsplit.

Quantity and UnitPrice must either both be absent or both be supplied, with a
UnitKey/UnitLabel when supplied. Linked catalogue lines copy item code/name/unit and may
still be recorded as an amount only. Unlinked lines have no catalogue code/name;
Description can identify a purchase without creating a reusable product. Only
linked lines with quantity and unit price participate in last-unit-price lookup
or supply a source for a remembered usual price. Category overrides on individual
lines are permitted without changing catalogue defaults.

Merchant, category and date reports aggregate all lines regardless of detail.
Product-level reports cover only linked item lines and must communicate that
scope; do not infer coffee/scone splits from a quick Eating out entry. Refining a
record later may change its category breakdown, but must not duplicate spending.

### Optional itemised workflow

1. Create a spending record with date, merchant, currency and optional notes or
   receipt/order reference. A new merchant can be created during entry.
2. Add an item by code/name, or create it inline. Prefill its category and unit,
   and its remembered usual unit price for this merchant/currency/unit when known.
   Show the last price paid and purchase date alongside it as separate information.
3. Enter quantity and actual unit price. Save actual values on the line. A price
   override does not silently update the remembered usual price. Offer an explicit
   Use as usual price next time action, including on the first purchase.
4. Prefill NetAmount from quantity × unit price using the receipt currency's
   rounding rule. Allow the actual discounted/rounded line amount to override it.
   Preserve both inputs and NetAmount; reports sum NetAmount, not both values.
5. Optionally enter ReceiptTotal and reconcile it against the line total. Surface
   differences rather than silently allocating a discrepancy. Saving a mismatch
   is allowed so entry can be completed later; show Unreconciled and the difference
   in entry/history views. Reports still sum lines only. A missing ReceiptTotal
   means Not checked, not Reconciled; matching totals mean Reconciled. Derive this
   status rather than storing an independently editable flag.
   Delivery, fees and other charges can be explicitly categorised lines.
6. Save the header and all lines atomically with revision checking for corrections.

### Usual price versus last price paid — agreed behaviour

Remembered usual prices are entry defaults, not assertions about today's actual
price. A receipt cannot reliably establish whether a discount is temporary.
Changing the actual price therefore never automatically replaces the usual price,
even when PromotionStatus is Unknown or No. The optional promotion status defaults
to Unknown; an unmarked receipt is not evidence that a price is regular.

Example: remember £3 per six-pack of eggs at Waitrose. A later purchase at £2 saves
£2 on that spending line and may be marked as a special, but the next purchase
still prefills £3 and shows Last paid £2 with its date. For a permanent change to
£3.20, the user explicitly chooses Use as usual price next time. That also handles
unknown specials without making the user identify every promotion correctly.

Use a unique (HouseholdId, SpendingItemId, MerchantId, CurrencyCode, UnitKey)
price record. Never reuse a different merchant's, currency's or unit's price
silently. If there is no matching usual price, require entry and offer to remember
it; do not silently adopt a possibly promotional last-paid price. Keep pack sizes
as distinct items where appropriate rather than mutating their meaning.

Last paid is read from saved item-linked Payment lines with quantity/unit price
for the same household, item, merchant,
currency and unit, ordered by purchase date, with a deterministic creation/line
tie-break for same-day records. Exclude voided records, refunds and records later than the entry's
purchase date when prefilling a backdated purchase. Show UnitPrice and NetAmount /
Quantity separately if a line discount makes the effective paid price different.

For a discounted line, explicitly remembering a usual price must make clear which
unit amount is being remembered; do not silently treat an undiscounted UnitPrice
as the effective paid price. Saving a purchase and a requested usual-price update
should be atomic, with concurrency checking for the remembered price. If an item
appears on multiple lines, choose one usual price explicitly rather than relying
on line order. An optional source-line link must match household, item, merchant,
currency and unit. The remembered amount is copied: correcting a source receipt
does not silently change it. Future LLM extraction follows the same explicit
usual-price update rule. Source links are informational; their correction/removal
policy is defined below so they cannot block normal receipt editing.

Code is a required user-facing code unique within the household's SpendingItems,
including retired items. The UUID is the database identity. Define trimmed,
case-insensitive code uniqueness explicitly; item names need not be unique.
BudgetItem codes have their own equivalent household uniqueness rule.

ReceiptDescription and MerchantItemReference belong to the saved line: the same
item may be printed differently or have different references at different shops.
ExternalReference on the header identifies the receipt/order, not a product.
A later merchant-item alias table can learn mappings without changing saved lines.

When supplied, Quantity is positive decimal, not integer (e.g. 0.75 kg). UnitPrice and NetAmount
are nonnegative, allowing free/promotional items. UnitLabel defines what one unit
means, such as each, kg, litre or pack of six. Different pack sizes can be separate
catalogue items; price-per-kg comparisons and unit conversions are later work.

UnitKey is a user-defined identity code, trimmed and lowercased consistently before
storage/comparison (e.g. kg or pack-6). UnitLabel is the user-friendly display label,
copied to saved lines and prices. The key, not the label, defines price identity:
changing kg to KG as a display label does not create a new history. Do not infer
that arbitrary keys such as kg and kilogram are aliases. Present the item's saved
key during entry instead of asking the user to retype units. A different pack size
or meaning requires a new item; existing unit keys cannot be repurposed once used.
No unit catalogue, conversion engine or automatic semantic normalisation is needed.

Proposed small refund model: Kind = Payment or Refund, with nonnegative line amounts
and the header kind supplying the reporting sign. OriginalSpendingRecordId is an
optional same-household link. Mixed purchase/return receipts can initially be
entered as two records. Refunds reduce actual spending in their recorded category
on the refund date. Discounts are already reflected in NetAmount.

### Create retries, corrections and voiding

The client assigns the SpendingRecord UUID before create and reuses it for retries.
An initial create inserts the whole record transactionally, including a server-
computed immutable hash of the canonical create request. A repeated create for
the same household/UUID and request hash returns the existing record's current
state without writing again; a different payload conflicts and must use the edit
endpoint with an expected Revision. Retries after editing/voiding never restore
the original data or reapply usual-price updates. A uniqueness constraint
and transactional conflict handling cover concurrent retries. Receipt references
are not unique and are not idempotency keys. Do not reveal another household's data
on UUID collision. Budget creates use the same UUID/retry convention, with the
additional effective-date uniqueness constraint.

Child-line edits, additions and removals check and advance the parent record's
Revision in the same transaction as the header/line changes. The same applies to
BudgetEntry changes and their parent BudgetSnapshot. A separate revision protects
the remembered usual-price row when explicitly updated.

In the record domain/persistence slice, currency/precision, kind and original-
payment association are fixed for a saved identity. Correcting those requires
voiding and creating a replacement with a new UUID. Merchant changes need explicit
replacement lines; date/reference/notes/receipt corrections and line-value corrections
preserve copied reporting context. Transactional service orchestration is implemented
in the record service slice and exposed by the record API.

Use a revision-checked void action for accidental duplicates or records entered in
error. Set VoidedAtUtc, retain the record and lines, and exclude them from totals
and last-price lookups. Void is not a refund: refunds represent money actually
returned. Normal correction can replace/remove individual lines, provided the
nonvoid record still has at least one line. Physical deletion of whole spending
records is not part of v1; setup retirement is a separate concern.

## Dated budget lifecycle

- A snapshot is a full replacement plan, not a patch of the previous one.
- EffectiveFrom is a date, with one plan per household per effective date. End is
  derived from the next snapshot; do not maintain a second editable end date.
- The active plan on a date is the latest EffectiveFrom on or before that date.
  Before the first plan, the budget is unknown, not zero. Future plans are allowed.
- A new plan explicitly selects a strictly earlier source, normally the latest
  plan preceding its effective date. By default, copy its selected entries and
  their saved metadata, amounts and frequencies. Do not silently refresh metadata
  from current setup. Show current setup differences and offer an explicit refresh
  for selected entries. With no source, selected items use current defaults.
- Show new setup items and retired source items during creation. New items are
  offered for inclusion, not silently added; ordinarily suggest excluding currently
  retired items. Permit deliberate inclusion of retired items in a backdated plan,
  and manual historical metadata where today's setup is inappropriate. Retirement
  alone must not make historical capture impossible.
- Included new items require an entered amount or remain unrecorded. Deliberate
  exclusion creates no entry. Show selected entries, additions and omissions before
  save. Refreshing currency, unit of recurrence or IntervalCount requires an explicit
  amount; never silently carry a number into a different currency or frequency.
- Record Confirmed, CarriedForward or Unrecorded provenance. Unchanged copied
  values retain their prior confirmation date; the user may confirm them explicitly.
  Explicitly entering/changing a nonnull amount also confirms it, records its
  confirmation date and clears its carry-source link. Clearing an amount makes
  it Unrecorded.
- Later setup edits or corrections to a source snapshot never rewrite saved
  descendant amounts or reporting metadata. Informational source links may be
  detached as described below. Correcting a budget requires its expected Revision.
- Retain old budget snapshots and their create identities/hashes; v1 offers no
  physical snapshot deletion. A new effective-dated replacement is the normal way
  to change the budget. Revision-checked correction remains available for errors
  in saved data, including removing an entry. A replacement preserves earlier
  plans, follows one-plan-per-effective-date uniqueness, and changes only the
  interval in which it is effective.
- A backdated plan changes which plan applies over its effective interval. Show
  that impact clearly before saving; this is distinct from changing saved copies.

Completeness is evaluated against saved entries only: an included entry with a
null PlannedAmount is unrecorded; zero is known; an absent entry is outside this
complete replacement plan. Adding a setup item later never makes an older plan
incomplete. An empty plan is an explicitly empty plan, distinct from having no
plan at all. Absence does not assert that the outgoing never existed or cost zero.
No extra persisted exclusion status is needed initially.

Example: a plan effective 1 January has electricity £200/month; a replacement
effective 15 February changes it to £20/month and carries the other amounts.
A £20 utility payment is a separate SpendingRecord. A lower actual payment does
not automatically rewrite the plan, and a lower planned amount is not evidence
that a payment occurred.

## Linking actual and planned spending

ExpenseCategory is the primary comparison key. Actual Food is the sum of recorded
food lines (less refunds); planned Food is the sum of active food BudgetEntries.
Entries are explicitly additive, within each category/currency and after any
display-frequency normalisation. Show the category total while editing. If the
user wants £500 Food overall, including a £50 subscription, enter £450 general
allowance plus £50 subscription. Entering £500 plus £50 means £550. Do not infer
containment, subtract named commitments automatically, or store another editable
category-total layer. Category names do not change these numerical semantics.

Defer SpendingLine.BudgetItemId and individual-outgoing matching. Category-based
comparison serves v1 without binding actual lines to mutable budget setup. A
future association needs explicit effective-date and historical-classification
rules; later setup changes must not invalidate saved spending.

Initial views can show a plan as of a date, monthly-equivalent costs and actual
spending over a date range. A monthly equivalent is a planning comparison, not a
schedule of cash due: annual / (12 × interval), monthly / interval, weekly × 52 /
(12 × interval). Keep original amounts/frequencies and avoid intermediate rounding.

Before implementing historical target-vs-actual charts, agree how to handle plans
that change mid-month and irregular bills: prorated allowances versus payments
due in particular months. Do not silently treat an annual bill's monthly equivalent
as the actual payment due each month. Due-date scheduling is deferred.

Actuals mean recorded spending, not guaranteed complete spending. An empty month
does not prove nothing was spent. Completeness/reconciliation with bank data is
outside the first version.

## Persistence and historical guarantees

- Enforce same-household references with composite foreign keys, following the
  existing EF model. Retain referenced setup identities; retirement replaces their
  deletion. Actual-record correction/void and budget-source retention follow the
  distinct policies below.
- Unique (BudgetSnapshotId, BudgetItemId), (SpendingRecordId, Position) and
  (HouseholdId, EffectiveFrom). Do not make SpendingItem unique per purchase:
  the same item can appear twice at different prices/discounts.
- Saved item/category codes and names, units, merchants, currency and budget
  frequency drive historical displays. Setup renaming/reclassification cannot
  silently move past expenses or rewrite past plans.
- One currency per SpendingRecord; remembered usual prices carry a currency.
  BudgetEntries may use different currencies. Keep totals separate by currency
  until explicit saved conversion rules are added; never sum GBP and ZAR directly.
- When present, carry-source links must refer to the same household and BudgetItem,
  an earlier plan and compatible currency/frequency. Prevent self-reference and
  cycles. These links are informational and may be detached during correction;
  their absence does not erase a saved CarriedForward origin.
- Index actuals by household/date, category and item; budget plans by
  household/effective date. Totals and variances are calculated, not stored tables.

### Values, precision and provenance

- Store money and quantities as exact PostgreSQL numeric / C# decimal. Proposed
  v1 input bounds: amounts (NetAmount, ReceiptTotal, PlannedAmount) are nonnegative
  and less than 10^12; unit/usual prices are nonnegative and less than 10^9;
  quantities are positive and less than 10^6. Accept at most six fractional digits
  for quantities, unit/usual prices and planned amounts. Reject excess precision
  rather than silently letting the database round inputs. These are technical
  bounds, not assumptions about a household's spending.
- Normalise currency codes to uppercase, trimmed three-letter codes. Entry
  validates against an explicit supported currency/minor-unit policy shared by
  API and UI. Do not guess two decimal places for an unsupported code; return a
  validation error. No country/currency is hard-coded as the household default.
  Actual NetAmount/ReceiptTotal must be in that currency's minor-unit precision.
  Prefill quantity × unit price rounded to that precision with midpoint rounding
  away from zero. Explicit receipt totals/discounted line amounts remain
  authoritative; never derive a new usual price implicitly from rounding.
- Frequency must be Week, Month or Year and IntervalCount a positive integer.
  Validate Kind and PromotionStatus against their defined enum values.
- Unrecorded means PlannedAmount, LastConfirmedDate and CarriedFromBudgetEntryId
  are all null. Confirmed means a nonnull nonnegative amount and confirmation date,
  with no carry-source link. CarriedForward means a nonnull amount and retained
  confirmation date; its source link is supplied when initially carrying the value,
  but may subsequently be cleared during correction. Such a detached entry remains
  CarriedForward, with its amount and original confirmation date unchanged; it is
  not newly Confirmed. When the link is present it identifies a strictly earlier,
  compatible entry. Explicitly entering/changing a nonnull amount, or confirming
  an unchanged carried value, makes it Confirmed, records the confirmation date
  and clears the carry-source link. Clearing an amount makes it Unrecorded.
  Confirmation dates describe user checking,
  not the effective start date; never invent confirmation dates from import time.
- A copied unrecorded entry stays Unrecorded. A carried entry takes the source
  amount/confirmation date at creation; future source corrections do not propagate.
  CopiedFromBudgetSnapshotId records the initial template independently of whether
  individual entries are carried, explicitly confirmed or omitted.
- Refund-original links, when supplied, target another Payment in the same
  household, never self or another Refund. Reject new links to voided payments.
  Refunds need not link to an original record (it might predate capture).
- Quantity/UnitPrice pairing, linked item metadata and UnitKey consistency follow
  the capture rules above. UnitKey is trimmed, nonblank and lowercased; codes use
  the specified case-insensitive uniqueness convention. SpendingItem codes have a
  64-character maximum; unit keys a 32-character maximum, enforced before indexing.
  Descriptive text follows the explicit shared whitespace policy documented in
  the setup slice. Unit meaning stays stable.

Database constraints enforce row-local rules (bounds, scales, null combinations,
enum values, normalised keys), uniqueness and household-scoped foreign keys.
Transactional application validation enforces cross-row facts: at least one line,
source kind/date/item/currency/unit compatibility, carried-value copying, creation
retry semantics and parent revision updates. Validate inputs before persistence so
unsupported precision does not get silently coerced by a fixed-scale SQL column.
Use checked decimal arithmetic and explicit validation errors for out-of-range
derived totals. No separate generic validation subsystem is needed.

### Reference and removal policies

| Reference | Policy |
| --- | --- |
| Setup identity referenced by saved data | Restrict deletion; retire the setup item instead. |
| SpendingItemMerchantPrice.SourceSpendingLineId | Informational. On removal of that line, or correction that makes item/merchant/currency/unit incompatible, clear the link and retain the copied usual price. Clear it on void of the source record too. Advance the price row's revision. |
| Refund OriginalSpendingRecordId | Retain reference to the original Payment, including if it is later voided. Label that state; do not automatically void a real refund. Parent records are retained, so the link never dangles. |
| BudgetEntry.CarriedFromBudgetEntryId and BudgetSnapshot.CopiedFromBudgetSnapshotId | Informational. Clear links that would become invalid during correction/removal of a source entry or correction of a snapshot's effective date. Preserve independent descendant amounts, metadata, confirmation dates and ValueOrigin. Budget snapshots themselves are retained. |

For the nullable usual-price source reference, use a household-scoped composite FK
with NO ACTION/Restrict in the database. In the same correction transaction, clear
SourceSpendingLineId on referencing price rows before deleting an old line, while
retaining the required HouseholdId. Do not apply blanket composite ON DELETE SET
NULL that would attempt to null the household key. The same transaction checks
affected price revisions, including concurrent explicit usual-price updates.

Apply the same household-scoped NO ACTION/Restrict FK and transactional detachment
pattern to budget source links: clear only the nullable reference, never HouseholdId.
Removing a source entry or correcting its identity/currency/frequency detaches any
incoming carry links that would become invalid. Correcting a receiving entry also
clears its own incompatible link; an explicit amount change confirms it under the
rules above. Correcting EffectiveFrom detaches any affected incoming/outgoing carry
or template links that would no longer point strictly backwards. Compatible links
may remain; source amount corrections alone need not detach them because copied
amounts are independent, not live equality constraints.

Save source corrections and affected link detachments atomically, checking and
advancing each affected parent BudgetSnapshot Revision. A concurrent edit causes a
conflict rather than a lost update. Detachment alone never changes a descendant's
amount, reporting metadata, LastConfirmedDate or ValueOrigin. Display a detached
CarriedForward entry as carried with source unavailable. Do not rebuild descendants
or automatically relink them to another entry. Show effective-interval changes
before saving date corrections; retain all budget snapshot UUIDs and immutable
CreationRequestHashes so delayed create retries resolve to the retained original
record instead of inserting it again.

Refund-original links retain their separate rules: reject changing a referenced
original Payment into a Refund. Budget provenance detachment does not weaken
household isolation or the validity of references that remain present.

The existing projection expense target is a manually supplied annual expense
amount with its own currency. Later, offer an explicit import from a selected
budget plan, preserving the source/date and conversion assumptions. Do not make
existing saved projections change automatically when budgets change.

## Suggested implementation slices

Build bottom-up: domain/schema/persistence first, then services and their
transactional rules, then API contracts/endpoints. UI follows once the backend
workflows are established. The broad feature sequence is:

1. Expense categories, merchants, SpendingItem catalogue and explicitly remembered
   merchant prices. Domain, EF mappings, migration and persistence tests are the
   implemented first slice; receipt-source links wait for spending records. The
   setup services and API endpoints are also implemented. Inline creation in the
   UI comes later.
2. Manual spending capture, correction and history with receipt reconciliation;
   record domain, mappings, migration and persistence tests are implemented;
   scoped services and HTTP endpoints are also implemented. History uses bounded
   parent-record paging and deterministic cursors with equal-date continuation
   tests. The fixed version-one hash fixture, client UUIDs, 201/200 replay distinction,
   record 409 mapping and omitted-versus-zero amount contracts are retained.
   Verify quick capture without catalogue items, category splits, mixed detail,
   atomic replacement of summary lines, fractional quantities, duplicate item
   lines, usual-price lookup exclusions for amount-only lines, price overrides,
   refunds, currency mismatches and historical metadata preservation. Verify
   concurrent create retries, unreconciled saves, parent revision conflicts,
   void exclusion, and clearing informational price sources on line removal.
3. BudgetItem setup and dated BudgetSnapshot capture/carry-forward/correction;
   verify effective-date selection, backdated retired items, source-metadata
   copying versus explicit refresh, deliberate omissions versus null/zero,
   additive category totals, stale edits and independent saved copies. Check
   confirmation on explicit amount entry/change, detached carry provenance,
   source-entry removal/currency/frequency and effective-date corrections, atomic
   revision checks for affected descendants, snapshot retention and create retries
   after replacement/correction.
4. Actuals and plan summaries by category/currency; agree period allocation rules
   before presenting historical target variances. Include actuals with no category
   budget, incomplete plans and reconciliation warnings visibly; do not infer
   complete actual spending from recorded receipts alone.
5. Later: receipt attachments and LLM extraction into a reviewable draft using the
   same capture API, merchant aliases, scheduled due dates, bank matching, person
   allocations and explicit projection integration.

Do not build LLM ingestion now. The saved raw receipt descriptions/references and
separate catalogue identities leave space for it without a speculative extraction
schema. Extracted prices or item matches should be reviewed before saving actuals.

## Deferred choices and features

- Future spending UI must use ordinary header/line corrections for minor edits.
  Explicit replacement refreshes saved setup metadata and regenerates lines, so
  it must remain a deliberate action. Retain the original create payload and UUID
  for retries; after an uncertain mutation response, reload the saved record
  before submitting another mutation with its current revision.
- Before large-scale reporting, measure representative queries and choose any
  additional indexes from their access patterns. Existing capture/history indexes
  do not justify speculative reporting tables or indexes.
- Choose prorated allowances versus due-date schedules before historical variance
  charts; basic capture and as-of plan summaries do not depend on this choice.
- Individual budget-item matching and person allocations require demonstrated need.
- Bank reconciliation and guarantees of complete actual spending remain deferred.
- Income, savings allocation, disposable-surplus/affordability forecasting and
  rental/property/tax accounting remain outside this plan.
- Receipt attachments and LLM ingestion remain later, reviewed-draft workflows.

The review's issues are resolved above as explicit v1 planning rules; deferred
reporting choices should not block the first capture/schema slices.
