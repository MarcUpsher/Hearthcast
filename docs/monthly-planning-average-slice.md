# Monthly planning averages

19 November 2026. Adds a read-only monthly planning summary to Budget overview
using the existing saved-plan API. No backend, schema, contract, dependency or
stored-value changes. Budget capture/history and source amounts are unchanged.

## Scope and meaning

The summary uses the single plan effective on the applied as-of date. The loaded
date, effective-from and exclusive end date remain visible. Changing the date
input does not relabel the old results before applying the selection. A mid-month
date selects a plan; it does not calculate a whole month's expected payments or
blend in preceding/future plans. There is no period proration, bank execution
forecast, actual-spending reconciliation or cross-currency conversion.

Separate monthly averages show regular commitments, spending allowances and
combined earmarked amounts for each saved currency. Allowances remain additional
to separately listed commitments. Retired setup definitions retain included saved
amounts. Omitted setup items do not inherit older values or enter the calculation.
Search/category/retirement filters change the detail list, not these totals.
Saved recurrence/currency/kind are authoritative even when setup has changed.

No effective plan means no monthly summary, rather than a zero budget. An empty
saved plan gets an explicit empty state. Included unknown amounts are counted:
unknown-only cells show Unknown; mixed cells show Known subtotal with the unknown
count. A known zero stays known. A purpose with no entries contributes zero within
a currency already present in the selected plan; no empty GBP row is invented.

Original amounts, recurrence totals and per-entry payment schedules remain
available below the monthly summary. The Calculation basis disclosure explains
the convention and the difference between spreading a cost for planning and its
payment due date, including the annual GBP 600 / monthly GBP 50 example.

## Calculation and display

For an amount charged/earmarked every n periods:

| Saved recurrence | Monthly planning average |
| --- | --- |
| Week | amount × 52 ÷ (12 × n) |
| Month | amount ÷ n |
| Year | amount ÷ (12 × n) |

52 weeks per year is an explicit planning convention. Particular calendar months
and years can contain a different number of scheduled payments. Amounts retain
their original recurrence; no payment anchor/date participates in this formula.

Exact fractions of scaled decimal integers retain repeating divisions through
aggregation. GCD reduction avoids unnecessary denominator growth. Monetary
arithmetic never passes through JavaScript Number. Each purpose subtotal and the
combined exact sum are rounded once for display, with positive midpoint ties away
from zero. The combined figure is calculated from unrounded values, not rounded
displayed subtotals; when those differ, a short rounding explanation is shown.

The existing supported-currency endpoint supplies current minor-unit display
precision (including zero-, three- and four-place currencies). Unsupported
historical currencies or unavailable/invalid display metadata use an explicitly
labelled six-place planning fallback. That metadata never rewrites saved values.
Original saved precision remains visible in the entry amounts.

Currency metadata loads independently of the mandatory plan/lookups. A stalled
or failed metadata request cannot block the overview; the fallback renders first
and refines if metadata arrives. Household generation and request-version checks
ignore late metadata from a previous read or a household switch/round trip.

## Verification and review

Pure calculation tests cover recurrence intervals through Int32.MaxValue,
repeating fractions, rounding after sums, additive purpose totals, independent
combined rounding, currency precision/fallback, large six-decimal values, zero,
unknowns, invalid recurrence and immutable input. Rendered tests cover table
labels, unknown/partial states, displayed rounding differences, current vs saved
recurrence, retired/omitted definitions, filter independence, no/empty plans,
unapplied dates, new effective plans, stalled/failed metadata and stale completions.
The screen performs no writes; tests use mocked transport, never live records.

Astra reviewed once and identified that optional currency metadata could stall
the whole overview. It now loads independently, with regression coverage for
immediate fallback rendering and later/stale precision updates. No second review
was requested. **602 frontend tests across 31 files pass** under `TZ=Europe/London`;
production build and whitespace checks pass. Backend tests are not rerun because backend code,
API contracts and schema are unchanged. No live browser visual inspection was
performed; rendered Angular/Material tests cover the UI.
