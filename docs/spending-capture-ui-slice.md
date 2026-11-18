# Spending capture and history UI

18 November 2026. Adds `/spending/records` through Spending → Purchases & history.
This is the separately authorised slice after the reviewed item catalogue. It
uses the existing capture/history/detail/void and exact usual-price lookup APIs.
No backend, schema or dependency changes.

## Entry and amounts

Purchases require a date, merchant, supported currency and at least one line.
Lines can be category/amount entries, catalogue items, or a mixture. Repeated items
and category splits are supported. Catalogue selection supplies its default
category and one unit; the category remains overridable. Quantity is in the
catalogue unit/pack shown on screen. Item lines require positive quantity and
actual unit price; quick lines need neither. Notes, receipt reference/total,
line description, receipt item reference and promotion status are optional.

Usual-price lookup is exact for the item/merchant/currency. Missing or failed
lookups permit manual price entry; there is no last-paid or cross-currency fallback.
Late lookups cannot overwrite manual unit prices or actual line-amount overrides.
Selecting another merchant clears an unedited suggestion and fetches the new exact
identity. Currency changes require confirmation when numbers are present, perform
no FX conversion and never round manually entered actual amounts to fit a currency.

Suggested line amounts multiply decimal strings using scaled BigInt arithmetic,
then round midpoint away from zero to the selected currency's saved minor units.
Actual line amounts remain authoritative and can override the calculated result;
Use calculated amount is an explicit action. Draft totals and receipt differences
use the same exact arithmetic. Zero and missing values are distinct. Currency
precision and domain bounds are checked before save, and a number that cannot
round-trip safely through the numeric JSON contract is rejected rather than
silently losing significant digits.

The currency-change confirmation includes a populated receipt total, including
explicit zero, even when no line amounts have been entered.

Receipt mismatch is shown before and after saving; it does not prevent capture or
replace the line totals. Missing receipt total remains unchecked. Capturing an
actual price or promotion never writes remembered prices.

## Saves and recovery

A client UUID is retained for the draft. Known validation/not-found rejections
leave fields editable and retain the identity. During a write, controls and
navigation are blocked. Success clears values and validation state by creating a
fresh form instance, then displays the saved record independently of history load.

Network/server errors, HTTP timeouts (including 408) and create conflicts retain an immutable submitted request
and lock its fields. Retry same purchase sends the same ID and values. Check saved
purchase reads that ID; finding it recovers the retained record without another
write. A 404 does not prove an earlier in-flight save cannot finish, so it retains
the exact-retry state. Abandoning an uncertain draft requires explicit confirmation
that a save may already have succeeded. No automatic retry or new ID is generated
to bypass an uncertain result.

## History and voiding

History uses complete bounded API pages (25 records) with optional inclusive date
filters and explicit void inclusion. Next/Previous preserve applied query options
and opaque cursor anchors. Changing filters requires Apply before page navigation;
new applied filters restart pagination. A failed page fetch retains the prior page
and continuation. History errors are separate from save success and setup errors.
Amounts display per record currency/precision, including refund signs and zero
counted amounts for voids; there is no mixed-currency grand total.

Details use saved merchant/category/item/unit labels, optional notes/references,
line values, promotion and reconciliation metadata. Refund records can be browsed
but refund creation and detailed corrections are deferred. Voiding an accidental
record requires confirmation and its current revision; it retains history and
does not create a refund. A failed/conflicting void requires an explicit fresh
record read before another attempt. There is no automatic retry using a newer
revision. Successful voiding retains the detail view and refreshes history.

## Scope and verification

The page integrates capture drafts and pending writes into route, household and
browser-unload guards. Household/draft/line/request generations reject stale
responses, including removed-line price lookups and prior detail/history reads.
History filters and navigation do not discard a capture draft.

Before review, all 258 frontend tests passed across 17 files, including decimal
rounding, zero/precision validation, quick/mixed entry, default overrides,
uncertain-save recovery, exact retries, scope races, bounded cursors, saved labels,
receipt mismatches, void conflicts and shell guards. Astra's single review found
three issues: timezone conversion of date-only values during British Summer Time,
HTTP 408 incorrectly treated as a definitive save rejection, and receipt-only
currency changes missing confirmation. Sol fixed each with regression coverage.
Date rendering now constructs an explicit UTC calendar date before formatting,
without interpreting a saved calendar day in the local timezone. Only known
400/404 create rejections unlock fields; other uncertain outcomes retain the
exact retry body and UUID.

After fixes the complete frontend suite passes **264 tests** across 17 files with
`TZ=Europe/London`, covering summer/winter history dates and both views. The
production build and staged whitespace checks pass. No second review pass.
Live browser access remains unavailable; rendered
Material component tests cover the controls and form lifecycle. Tests do not
create or void application-database records.

The subsequent [form usability update](spending-form-usability-plan.md) introduces
explicit item/category line types, searchable name-first item selection with
linked category filtering/locking, and one-row resizable receipt notes. Saved
classification and amount-only historical item lines remain supported.
