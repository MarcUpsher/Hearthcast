# Spending records — domain and persistence

17 November 2026. Next bottom-up slice of the
[schema plan](spending-and-budget-schema-plan.md). This adds SpendingRecord,
SpendingLine, EF mappings, AddSpendingRecords and optional usual-price source
links. The subsequent [record services](spending-record-service-slice.md) are now
implemented. HTTP capture, UI, budgets and receipt ingestion remain deferred.
The migration is tested in disposable databases, not applied to the running app.

## Capture and amounts

The record constructor takes a nonempty client-generated UUID, resolved merchant,
date, supported currency, lines and a canonical SHA-256 create-request hash.
Headers copy merchant name and save optional notes/reference/receipt total,
Payment/Refund kind, original-payment link, timestamps and revision.

Every record has at least one categorised line with a nonnegative NetAmount.
Quick amounts, category splits and detailed products share this same structure;
catalogue identities are optional and repeated products are allowed. Lines save
category name and, when linked, item code/name/unit. Category overrides do not
change the catalogue item. Unlinked quantified lines supply their own unit key
and label. Quantity and UnitPrice are either both present or both absent; a
quantity requires a unit. Linked amount-only lines remain valid.

Amounts use exact numeric/decimal, are nonnegative and below 10^12. Quantities
are positive and below 10^6; unit prices are nonnegative and below 10^9. Quantity
and unit prices allow six decimals. Actual line/receipt amounts must conform to
the saved currency precision. Invalid precision is rejected rather than rounded.
Optional descriptions, receipt descriptions, merchant item references and notes
use the existing shared whitespace policy. PromotionStatus is Unknown/Yes/No and
does not automatically update usual prices.

NetAmount is authoritative. SuggestedAmount rounds quantity × unit price using
midpoint-away-from-zero to the saved precision; it does not overwrite a discount.
EffectiveUnitPrice divides NetAmount by Quantity when known. TotalAmount sums
lines; CountedAmount negates refunds and excludes voided records. ReceiptTotal
is a reconciliation input, not another counted amount. NotChecked, Reconciled
and Unreconciled statuses are derived; mismatches can be saved.
Aggregate totals require the full Lines collection to be loaded. There are no
stored summary totals; later services may aggregate directly in SQL.

## Currency policy

SpendingCurrencyPolicy pins numeric minor-unit entries from the
[SIX ISO 4217 List One](https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml),
published 17 September 2026. It is checked-in policy data, independent of locale
display/cash rounding, with no runtime web or exchange-rate request. Unknown
codes and entries without numeric minor units are rejected. The list can be
extended deliberately, including historical codes when required; the schema has
no country boundary or GBP default.

The policy exposes supported codes/digits for future API/UI consumers. JPY uses
zero digits, GBP/ZAR two, KWD three and CLF four. Headers save CurrencyMinorUnits
so future policy changes cannot reinterpret saved receipts. Lines copy the same
currency/precision, enforced by a composite parent FK; this makes line precision
checks enforceable in PostgreSQL. Setup prices still validate currency shape only.

## Corrections and sources

Aggregate mutation methods check/advance the parent revision after successful
validation. Invalid input leaves the aggregate unchanged.

- CorrectHeader changes date, receipt total, reference and notes without refreshing
  saved merchant/item/category context.
- CorrectLine changes amounts, quantity/unit price, promotion status and notes,
  preserving saved names and units.
- ReplaceLines deliberately replaces a quick summary with splits/products and
  returns old rows for removal. The summary is not retained as another counted line.
- Replace changes merchant/header metadata with explicit replacement lines and
  copied context. Currency/precision, kind and original-payment association are
  fixed for this identity in this slice; mistakes there need void/new record.
- Void retains lines, raw amounts and create identity/hash but counts zero.
  Voided records cannot be edited. This is distinct from actual money refunded.

Refund originals must be a different Payment in the same household. The domain
rejects new links to voided payments. A companion OriginalSpendingRecordKind and
composite FK enforce original kind/household in SQL. Existing links survive later
voiding, and a referenced Payment cannot be reclassified or deleted. Amounts remain
nonnegative; Refund supplies the reporting sign.

RememberFrom explicitly selects a usual price from an owned, itemised, nonvoid
Payment line matching item/merchant/currency/unit. The chosen unit amount is
explicit, allowing a deliberate choice of effective discounted versus nominal
price. Capture/correction never updates it automatically. SourceSpendingLineId is
nullable with a household-scoped restrictive FK. ClearSource retains the copied
price and advances its revision; manual price edits also clear old attribution.
Source amount changes do not propagate into remembered prices.

## Persistence versus service responsibilities

Database constraints protect row shapes, exact amounts, precision, enums,
canonical text, valid dates/hash/revision, household scope, item/unit compatibility,
original-payment kind and unique record positions. Date/merchant/category/item
indexes support later queries. All deletes use restrictive relationships.

Domain operations require at least one line and touch the parent's EF concurrency
token on line edits. Persistence tests verify that a stale parent write rolls
back its child writes. Raw SQL is not a supported aggregate-editing workflow;
cross-row application rules are not replaced by a generic trigger subsystem.

The [service layer](spending-record-service-slice.md) loads full aggregates and
coordinates mutations in a single transaction. On source removal, void or incompatibility it detaches
referencing usual-price sources, retaining HouseholdId and checking price revisions,
before removing source rows. A detach-save, replace-save transaction is tested here.
New nonvoid refund targets and eligible price sources need validation inside that
transaction. The service layer covers failed-save rollback and concurrent source changes.

Create-request hashes are validated/stored here, not calculated from mutable
setup metadata. Canonical request hashing, create-ID retries, last-paid queries
and atomic receipt-plus-usual-price updates are implemented in the service layer. There is no public HTTP
record capture operation yet. Duplicate receipt references/merchant/date combinations
are allowed and are not idempotency keys.

## Verification and next work

All 66 new domain/persistence cases pass: mixed detail, repeated products, discounts,
fractional units, currency precision, unknown currencies, metadata preservation,
invalid-mutation atomicity, mismatches, refunds, voids, parent concurrency, source
detachment, scoped FKs, direct-SQL bypass attempts, duplicate references and migration
rollback/reapplication preserving existing setup/price values and revisions.

At completion of the domain/persistence slice, full backend verification passed:
1,118 tests (732 data/domain, 386 HTTP API),
with no failures or skipped tests. This builds all backend projects and includes
the existing financial-history and migration/model checks. The existing NU1900
warning remains for unavailable NuGet vulnerability metadata; no dependencies
were added.

The [record services](spending-record-service-slice.md) are implemented. Record HTTP
endpoints follow; UI and budgets remain deferred.
