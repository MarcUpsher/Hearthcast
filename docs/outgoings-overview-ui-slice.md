# Current outgoings overview

19 November 2026. Adds `/spending/overview` and its Spending navigation link.
Uses the existing current-outgoings endpoint; no backend, schema or dependencies
change. Links lead to existing outgoing definition and dated-plan screens.

## Meaning and scope

Reads use an explicit local as-of date and `includeRetired=true`. Saved plan
entries supply the amount, currency, recurrence, names, category, merchant, notes,
value origin and confirmation date. Current setup never replaces saved amount
context. Missing entry, included unknown amount, explicit zero, no effective plan
and an empty replacement plan are distinct states. Omitted entries do not inherit
older values. Current shared lookups label only rows without a saved entry.

Totals use all included entries, including retired setup items, and remain
unchanged by search/category/retirement filters. They preserve exact decimals and
group by currency, frequency and interval. Unknown values make a group's subtotal
visibly partial. The page shows plan completeness, unknown/omitted counts and the
effective interval. No conversion, payment allocation or spending comparison is
performed. All read rows come from the uncapped current-outgoings response.

Payment schedules are separately labelled as current setup context. They derive
the next calendar date on or after the displayed as-of date using the existing
anchored schedule helper. They do not assert historical timing or the actual
execution date. Retired items retain saved amounts but have no upcoming date.
Unset/exhausted schedules are explicit. Different saved/current recurrence and
currency are called out. A date on or after the plan's exclusive end warns that
its amount may differ; it is not presented as an amount due beyond that plan.
Weekend/bank holiday adjustments are outside this scope.

The read-only screen supports date selection, Today, retry and local list filters.
Unapplied dates are clearly labelled; rows/summaries are hidden during loading
or after a failed request. Version and household generation checks reject older
responses, including switching away and back. Household switching resets filters,
date and results, without draft prompts or writes.

## Budget work following this slice

The user clarified that known recurring commitments (rent, internet, utilities)
and proposed category allowances (for example Food: £400 for a chosen period)
serve different purposes. Allowances should be expressed against spending
categories and a period, without inventing a merchant or debit date. This slice
does not add targets or change existing outgoing definitions/plans.

The next budget design should agree how dated category targets are entered and
carried forward, their period/frequency, and how they coexist with recurring
commitments without double-counting. Actuals already come from recorded spending
by saved category/currency over an explicit inclusive range. Mapping targets into
arbitrary report ranges and comparison/allocation rules remains a separate
decision. No target schema or automatic comparison policy is decided here.

## Verification

Component tests cover saved/current context, exact numeric reads and grouped
totals, zero/unknown/omitted/no-plan/empty-plan states, retired entries, schedule
clipping and finite bounds, interval warnings, filters, rendered dates, stale
household/read responses, retry and empty states. Shell tests cover navigation
and household switching without draft prompts. Test writes are mocked; this
screen performs no mutations. Backend tests are not rerun because backend code
and contracts are unchanged. Rendered Material/component tests cover workflows;
no live browser visual inspection was performed.

Astra reviewed once and found a category filter that could become invisible after
a date reload, and search falling back to a hidden current note when a saved
entry's note was null. Both are fixed with regression cases. After fixes,
**537 frontend tests across 28 files pass** under `TZ=Europe/London`; the
production build and whitespace checks pass. No second review was requested.
