# Interactive modelling screen

> Historical implementation record. Start with the [current modelling guide](modelling.md)
> and [native currency contracts](native-currency-projection-slice.md). The original
> single-currency or initial-screen details below include superseded behaviour;
> retain them for design history, not as current payload examples.

`/modelling` is a lazy Angular Material page linked from the main navigation.
It uses the latest recorded snapshot by default and the stateless projection API.
No preset saving/loading controls are introduced yet; those are the next UI slice.

The assumptions panel remains beside the graph on desktop with its own scroll
area, and stacks above it on smaller screens. Duration is a number field, not a
slider. Changes recalculate after a 300 ms debounce. Every edit invalidates earlier
responses immediately, including edits that make the form invalid. Accepting a new
response resets the chart cursor, including hover events made on the previous chart
while a shorter projection was pending. Snapshot and
household loads have independent guards; leaving the route clears pending timers
and prevents outstanding requests updating a destroyed view.

## Inputs and selections

- Snapshot selector: all saved snapshots, latest first as returned by inventory.
- Projection currency: snapshot native and saved quote currencies are suggested,
  with the shared currency list available independently of holdings. An empty
  snapshot starts with a blank currency so the user explicitly chooses the units
  before modelling contributions from zero. Switching snapshots does not assign
  a currency to contributions entered while the currency was blank. A previously
  chosen currency is retained across historical snapshots rather than silently
  reinterpreting contribution amounts; missing rates are shown by API diagnostics.
- Duration: 10 years initially, editable 1–100 whole years.
- Investments/savings: explicitly selected groups, grouped under copied categories;
  the same group cannot be selected for both buckets.
- Investment/savings contributions start at zero. Illustrative nominal returns
  start at 5%/2%, with 3% inflation; these are labelled as editable assumptions.
- Financed assets are explicitly selected from copied household entries. Each has
  its own native-currency payment, loan interest and appreciation fields, initially
  zero with a prompt to set them. Starting values/debt are displayed for reference.

Group classification is never inferred from names. No selections means the page
prompts for starting holdings; entering contributions alone allows modelling new
money from zero. Missing selections are retained across snapshot changes so the
user can remove them or choose a compatible snapshot. Live setup metadata is not
used. Household switches reset the scenario. Selections/assumptions remain only in
memory and are discarded when leaving the page in this slice.

The inflation switch operates locally on the two returned series and makes no API
request. Investment/savings inputs represent starting-snapshot purchasing power;
their nominal monthly contributions increase with the scenario inflation rate in
every currency pot. Loan payment budgets remain fixed in nominal terms, including
amounts redirected to investments after payoff. API errors and invalid input
clear results; while a valid recalculation is pending the page explicitly labels
any previous results still shown. Starting/modelled totals use the result currency,
so a pending currency change never relabels old amounts with new currency symbols.

## Results

An optional [expense target](projection-expense-target-slice.md) compares selected
investments (and optionally savings) with inflation-indexed spending at a chosen
withdrawal rate. It adds a guide, first-crossing marker and status card without
changing projected balances or starting withdrawals.

The graph is followed by a [growth breakdown](projection-growth-breakdown-slice.md)
showing how starting net worth becomes the projected final amount. Its nominal
and inflation-adjusted views use the existing display toggle and reconcile to the
projected-total card. No additional assumptions or tracking fields are required.

The graph shows smooth solid lines for investments, savings, financed equity and
modelled total. It includes 10% axis headroom, hover readings for all four values
at the nearest month, and keyboard left/right inspection. Currency values reuse
existing formatting with symbols and spaced thousands. The timeline starts at the
snapshot date and ends at the projection horizon.

Loan payoff months have focusable diamond markers on the combined equity line.
Hovering, focusing or activating a marker shows its date and the assets paid off;
loans paid off in the same month share a marker. An already-paid loan is marked at
the start, and loans unpaid within the horizon have no marker. Markers follow the
nominal/inflation-adjusted equity display. Equity continues after payoff as the
asset's full value, growing at its own asset-growth assumption; inflation is a
separate display adjustment.

Payoff marker verification: all 106 frontend tests and the 31 existing pure
calculator tests passed, as did the production frontend build. Chromium verified
marker labels, hover details, keyboard focus, inflation-adjusted positioning and
the 430px layout against generic in-memory sample data. No backend/schema changes
were required.

Starting and final modelled totals, loan payoff dates and non-amortising warnings
are displayed alongside the graph. Missing balances/rates/selections block plotting
and appear as API data notices; carried values remain usable with warnings. The
page states that modelled totals cover selected holdings only. A disclosure lists
omitted or household-excluded snapshot entries. Snapshot and rate values are never
edited or saved by this screen.

## Verification and local deployment

Frontend tests cover latest-snapshot loading, explicit selections, debounce/payload,
invalid input during requests, out-of-order results, historical missing selections,
currency retention, household/snapshot races, local inflation switching, errors,
blocked data, and route cleanup. Existing frontend tests are included.

The local PostgreSQL database was backed up to the ignored `backups/` directory
before rebuilding the API and applying the previously reviewed additive preset
migration. Historical snapshot values were not changed. The local web container
was rebuilt for browser verification. No new backend schema or API code is added
by this slice.

Verification results: all 82 frontend tests passed (15 new modelling/review tests plus
67 existing tests). Production frontend builds passed, including the deployed web
container. Chromium verified live graph rendering, hover readings, duration-driven
recalculation, inflation switching without an API call, native loan controls/payoff
output, and a 430px layout with stacked controls and no page overflow. Screenshots
remain private under `/tmp`; they are not repository artifacts. `git diff --check`
passed. Separate backend suites were not rerun for this frontend-only change; the
underlying HTTP slice already passed its 313 integration tests.

Review regressions also cover choosing a currency on a fresh empty snapshot,
retaining unassigned units when switching snapshots with entered contributions,
and resetting hover when a shortened response replaces an interactive older graph.
