# Recorded spending by category

`SpendingCategorySummaryService.GetAsync` reads nonvoid spending records for one
known household and an inclusive `DateOnly` range. Both dates are required; an
invalid/default date or reversed range raises `InvalidValueException`, while an
unknown household raises `ResourceNotFoundException`.

Totals group authoritative saved line net amounts less assigned receipt discounts
by saved category ID and currency, irrespective of historical differences in saved currency precision.
Each group's display precision is the maximum saved `CurrencyMinorUnits` among
its contributing lines; the exact decimal sum is not rounded. Refund lines
subtract from their own spending date. Historical category
names are retained; where the same category/currency group has multiple saved
names, the report uses the name on the most recent record (date, creation time,
record ID, then line position break ties). Rows sort by currency, displayed name,
then category ID. Checked decimal addition preserves exact values and lets
unrepresentable totals raise `InvalidValueException`, following the existing
summary-service overflow convention. Zero and negative net groups are retained.

The report includes counts of eligible records in `NotChecked`, `Reconciled` and
`Unreconciled` states. Receipt warnings are emitted once per included record when
the saved receipt total differs from the unsigned recorded total after receipt discounts; difference
is receipt total minus recorded total, including refunds. Receipt discrepancies do
not change category totals. An empty report describes recorded spending only and
does not assert that every purchase was captured.

The read uses no-tracking split queries inside a repeatable-read transaction, so
parents, lines and discounts share one snapshot without multiplying sibling rows.
Totals and reconciliation use that same selected record set. This service does not write
spending, price, revision or budget state. The subsequent
[summary API slice](spending-category-summary-api-slice.md) exposes this service
without changing its calculation rules.

The three report records live in matching feature-owned files:
`SpendingCategorySummary`, `SpendingCategoryTotal` and `SpendingReceiptWarning`.
Warnings retain the existing unsigned receipt-minus-recorded-total difference even for
refunds; the signed category total is a separate calculation.

## Verification

Review expanded the original two tests to 19 PostgreSQL-backed cases. All 19
passed, covering inclusive arbitrary ranges across a year boundary, a single leap
day, default/reversed dates, unknown and empty household IDs, empty results,
household isolation, quick/split/itemised capture, price overrides and repeated
items, separate currencies and exact decimals, voided payments/refunds, linked
refunds after original-payment voiding, zero/negative groups, once-per-record
receipt warnings, historical category identity after setup changes/retirement,
label tie-breaking and deterministic row ordering. A range larger than a history
page verifies full inclusion, a single records/lines query and unchanged saved
revisions and usual prices.

The full Data suite also completed successfully: **988 passed, zero failures or
skips**, in approximately 2 minutes 41 seconds. The focused run's TRX is
`/tmp/hearthcast-summary-review/spending-category-summary.trx`; the full suite's
TRX is `/tmp/hearthcast-summary-review/data-suite.trx`. The normal NuGet `NU1900`
vulnerability-feed network warning was present. No dependencies or schema changes
were introduced.

## Final backend review follow-up

The review found that including saved precision in the grouping key could split
one historical category/currency into multiple rows. Grouping now uses only
category ID and currency; maximum contributing saved precision controls display,
without rounding the sum or selecting category labels independently per precision.
Two persistence-backed regression cases cover precision increasing and decreasing
between records, exact three-decimal totals and the latest saved category label.
Schema checks and foreign keys remain enabled for these historical fixtures.

After this change, **21 summary-service tests and 24 summary API tests passed**
with zero failures/skips. Results are in `/tmp/hearthcast-summary-review-followup/`.
The full-suite counts above describe the earlier verification; the follow-up reran
the affected summary tests. Whitespace checks passed.

Loading full aggregates remains acceptable for the initial range-based report.
Projecting only required fields or aggregating in SQL is a future optimisation
if data volume warrants it; preserve consistent totals/warnings and full-range
coverage rather than silently limiting records.

## Receipt discount extension

The [receipt discounts and last-price slice](receipt-discounts-and-last-price-slice.md)
extends this contract with category deductions, net recorded totals and explicit
historical price assistance. Earlier verification counts above describe their
original checkpoints.
