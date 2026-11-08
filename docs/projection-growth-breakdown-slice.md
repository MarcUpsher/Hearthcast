# Projection growth breakdown

Status: implemented; verification results are recorded below.

## Purpose and scope

Explain how a modelled starting net position becomes the projected final net
position. The breakdown covers the same selected holdings, currencies and horizon
as the existing graph. It explains simulated changes; it does not analyse actual
historical investment performance.

No NAV prices, units, transaction history, security identifiers, new tracking
fields, preset settings, database tables or migrations are required. Existing
projection assumptions and calculation order remain unchanged.

## User-facing breakdown

Add a compact table below the projection graph, before loan payoff details, titled
"Where the growth comes from". Show the result's starting/end dates and currency.

| Row | Meaning |
| --- | --- |
| Starting modelled net worth | Investments + savings + financed asset values − outstanding loans at the starting snapshot. |
| Investment returns | Gains/losses on all investment balances, including returns on subsequent contributions and reinvested loan payments. |
| Savings interest | Gains/losses on savings balances. |
| Financed asset value change | Appreciation/depreciation of the selected financed assets. |
| Investment contributions | Actual modelled regular contributions, including inflation indexation. |
| Savings contributions | Actual modelled regular savings contributions, including inflation indexation. |
| Loan balance reduction | Principal reduction; negative if unpaid interest causes debt to increase. |
| Reinvested loan payments | The unused payoff-month budget and released payments added to investments afterwards. |
| Inflation effect | Separate purchasing-power effect, shown in the inflation-adjusted view. |
| Final modelled net worth | The same final value shown by the graph and projected-total card. |

Changes are signed, allowing losses, depreciation, deflation and growing debt.
Keep zero contribution rows visible so a no-contributions scenario is explicit.
Use the existing currency formatting. Totals are calculated before display rounding;
displayed rows may have small rounding differences.

Mortgage interest is not itself wealth creation. Only the reduction in loan
balance contributes to net worth. Interest charged can remain explanatory text;
do not add total mortgage payments as another positive row. Available-to-withdraw
money is not an additional asset or contribution.

The reinvested-payments row contains cash added, not the eventual value of that
cash plus its returns. Those returns are already in Investment returns. This
avoids counting them twice.

## Reconciliation and inflation

In nominal money:

```text
Final = Starting
      + Investment returns + Savings interest + Financed asset value change
      + Investment contributions + Savings contributions
      + Loan balance reduction + Reinvested loan payments
```

For the inflation-adjusted table, retain the actual starting net worth. Convert
each month's changes using that month's cumulative inflation factor, then include
a separate inflation effect on the previous month's net position. Do not simply
divide all cumulative rows by the final inflation factor: that would misstate the
purchasing power of contributions when they were made.

With `F[t]` the cumulative price factor, `N[t]` nominal net position and `D[t]`
the month's nominal change:

```text
Real change[t] = D[t] / F[t]
              + N[t−1] / F[t] − N[t−1] / F[t−1]
```

Apply the first term to each change component and accumulate the second as the
Inflation effect. Then Starting + all real change rows + Inflation effect equals
the final inflation-adjusted net position.

This convention expresses contributions in purchasing power when contributed.
For a contribution entered as 1,000 in starting-snapshot money, the real regular
contribution row grows by 1,000 per month at the same-currency rate, even while its
nominal amount increases. Fixed nominal released loan payments gradually have
less real purchasing power under positive inflation. The inflation-effect row is
signed: deflation or a negative starting/monthly net position can reverse its sign.

All currency conversions use the projection's existing fixed snapshot quotes.
The existing single scenario inflation rate remains the assumption for all pots.
The inflation effect is purchasing-power accounting, not a cash payment.

## Calculation and HTTP shape

Calculate the breakdown alongside the existing monthly simulation in the pure
calculator. Reuse its actual indexed contribution amounts, cash flows, balances
and inflation factors. Do not run additional counterfactual projections or
reimplement compounding in Angular.

Monthly components can be reconciled against the produced balances:

- Investment returns = investment balance change − regular investment
  contributions − reinvested loan payments.
- Savings interest = savings balance change − regular savings contributions.
- Financed asset value change = current valuation total − previous valuation total.
- Loan balance reduction = previous debt total − current debt total.

Accumulate nominal and inflation-adjusted components during the run. The
immutable output types are `ProjectionGrowthBreakdown` for a set of starting,
component and final amounts, and `ProjectionGrowthSummary` containing `Nominal`
and `InflationAdjusted` breakdowns. `ProjectionGrowthAccumulator` performs the
monthly accounting independently of the existing balance updates.

Add the summary to `ProjectionResult` and as an additive `GrowthBreakdown` property
on the existing projection HTTP response. It is null when preparation is blocked,
just as Months is null. No new endpoint or persisted summary is needed.

The first slice returns the complete horizon summary only, rather than a second
breakdown for every chart point. The current display toggle selects the returned
nominal or real summary without another request. During recalculation, use the
previous result's currency/dates and the existing previous-results notice. Hide
the breakdown for blocked or cleared results.

Existing presets and request bodies retain their shape. The page continues to
save only assumptions, not calculated breakdowns.

## Implementation order and acceptance checks

1. Add pure result records and calculator accumulation. Prove that the component
   rows reconcile to every existing final result, without changing balances,
   contribution indexing, loan payoff dates or payment redirection.
2. Add the summary to the HTTP response and verify saved and unsaved scenarios
   produce identical breakdowns; blocked runs return null.
3. Add the Angular table and formatting. Test toggle behaviour, pending/error
   states, negative/zero values and agreement with the projected-total card.
4. Run the affected backend projection and frontend suites, build production
   output, and inspect the desktop and narrow layouts with generic sample data.

Calculation regressions should include zero growth/contributions, contributions
only, growth on existing and newly contributed money, savings interest, negative
returns, asset appreciation/depreciation, multiple currencies and viewing-currency
changes, independent loan payoffs, payoff-month surplus, already-paid loans,
capitalised interest, zero inflation, positive inflation and deflation. Include a
constant-real-contribution case and negative net worth. Use decimal tolerances
consistent with the existing calculator; do not disguise unexplained residuals
as an Other row. Preserve the current numeric-limit error behaviour.

## Verification

All 165 backend projection tests passed: 113 data/calculation tests and 52 API
tests. They include fourteen new breakdown cases covering reconciliation, cash-flow
attribution, inflation/deflation, negative net worth and currency changes. Existing
balance and payoff regressions passed. The solution and production frontend builds
passed.

All 112 frontend tests passed. Six additional cases check formatted/zero/negative
rows, local inflation switching, unchanged starting value, final-card agreement,
pending currency/date labels, blocked/cleared results and sub-cent signed-zero
formatting. The existing report fixture now has consistent nominal/real starting
values and inflation-adjusted totals.

Chromium verified both tables reconcile within display rounding, both final values
match the projected-total card, the starting value is unchanged between views,
switching inflation display makes no request, the inflation row appears only in the
real view, and the 430px layout has no page overflow. Browser fixtures were generated
by the actual calculator with generic sample data; no database was accessed and no
presets or snapshots were saved by these checks. Screenshots remain under `/tmp`.

The local API and web app were rebuilt. A calculation-only request for the saved
live preset verified that both breakdowns reconcile and exactly match the graph's
final totals. No preset settings, snapshots or schema were changed by this slice.
