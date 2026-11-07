# Native currency projection pots and regular/extra loan payments

The projection model now keeps investments and savings in native currency for the
whole calculation. The household can invest in GBP and ZAR simultaneously, each
with independent contributions and nominal returns. Changing the viewing currency
changes converted totals, not the units of contributions or the loan destination.
Exchange rates remain fixed using the starting snapshot. Currency depreciation
and inflation-linked exchange rates are future work, not silently inferred here.

## Persisted schema and API inputs

`ProjectionPreset` retains name, household, viewing `CurrencyCode`, duration,
viewing-currency inflation/display choice and UTC timestamps.

`ProjectionPresetCurrency` has composite key `(ProjectionPresetId, CurrencyCode)`:

- Monthly investment contribution and annual investment return.
- Monthly savings contribution and annual savings interest.

Every amount is in that row's currency. Currency codes are normalised before
checking uniqueness. Each configured currency has an investment pot and a savings
pot; zero contributions and balances are allowed. Group selections still map to
Investment or Savings, independently of currency. Snapshot entries determine the
native currency, not their names or inferred underlying fund exposures.

`ProjectionPresetFinancedAsset` retains the native regular payment
`MonthlyLoanPaymentInAssetCurrency`, annual loan interest and asset appreciation,
and adds:

- `ExtraMonthlyLoanPaymentInAssetCurrency`: optional economically, explicitly zero
  or positive in saved/API settings.
- `InvestmentCurrencyAfterPayoff`: a configured investment currency pot.

A composite FK to the same preset's currency row prevents a destination referencing
another preset. Changing/deleting settings remains an atomic aggregate operation.
Deleting a preset removes only its settings; linked financial records remain.

The global contribution/return fields have moved under `assumptions.currencies`.
The HTTP routes are unchanged. Loan requests require the extra amount and destination.
Old request bodies are rejected rather than silently reinterpreted. Current example:

```json
{
  "inputs": {
    "assumptions": {
      "currencyCode": "GBP",
      "durationYears": 10,
      "annualInflationPercent": 3,
      "showInflationAdjustedValues": false,
      "currencies": [
        { "currencyCode": "GBP", "monthlyInvestmentContribution": 1000,
          "annualInvestmentReturnPercent": 5, "monthlySavingsContribution": 0,
          "annualSavingsInterestPercent": 2 },
        { "currencyCode": "ZAR", "monthlyInvestmentContribution": 500,
          "annualInvestmentReturnPercent": 8, "monthlySavingsContribution": 0,
          "annualSavingsInterestPercent": 4 }
      ]
    },
    "groups": [],
    "financedAssets": [
      { "financedAssetId": "22222222-2222-2222-2222-222222222222",
        "monthlyLoanPaymentInAssetCurrency": 20000,
        "extraMonthlyLoanPaymentInAssetCurrency": 2000,
        "annualLoanInterestPercent": 12, "annualAssetAppreciationPercent": 2,
        "investmentCurrencyAfterPayoff": "GBP" }
    ]
  }
}
```

Amounts/rates above are illustrative. Use actual group/asset IDs from the household.
Save the same inputs with a name through the existing preset create/update routes.

## Calculation and preparation

Preparation sums selected ordinary entries by both bucket and native currency.
There must be a configured currency row for each selected native ordinary currency;
missing settings are explicit blocking diagnostics. Loan valuation and debt remain
native too. The calculator requires known starting positions per configured currency.

Currency positions carry native investment/savings balances and a fixed quote into
the viewing currency. A zero, unused pot may retain a null quote: it has no existing
balances, contributions or possible redirected budget, so no rate is invented or
needed. Any used pot requires a valid quote; selected entries still follow existing
missing-rate diagnostics and conversion precision checks. Unknown values never
become known zeros, and absent loans never become paid-off loans.

Each month:

1. Native pots grow at their own nominal effective annual rates compounded monthly.
2. Native loan interest is opening debt times annual nominal loan rate / 1200.
3. Regular plus extra payments cover interest and then reduce principal. No separate
   extra contribution is deducted from investment or savings pots: these budgets
   represent new household income, not an unmodelled transfer from existing wealth.
4. The unused payoff-month budget, and the full combined budget in later months,
   converts into the chosen native investment currency and is added at month-end.
5. Regular investment/savings contributions are added in their own currencies.
6. Viewing totals convert the resulting native balances using saved fixed quotes.

For R1m debt, R20k regular payment, R2k extra and an illustrative 12% annual rate,
month 1 charges R10k interest and repays R12k principal, leaving R988k. With R20/GBP,
a later full R22k released budget contributes GBP1,100 into a GBP destination pot.
A ZAR destination receives R22k instead. Both budgets redirect after payoff; the
final month's debt is paid only once and its leftover is redirected once.

The output includes `months[].currencies` for native balances and redirected amounts,
in addition to converted nominal/inflation-adjusted totals. Per-loan output records
both native unused budget and `InvestmentAmountRedirected` with its destination
currency. The model still covers selected holdings only, and does not infer market
exposure of underlying funds or model withdrawals, fees, tax or currency forecasts.

## Migration preservation

`AddProjectionCurrencyPots` creates currency rows from each old preset's viewing
currency and four scalar contribution/return fields before dropping those columns.
Existing regular native loan payments are preserved, extra payments start at zero,
and the old viewing currency becomes the old loan's explicit investment destination.
Selected groups also receive zero-contribution rows for their known native
ordinary currencies (including historical copied entries), inheriting the old
shared returns. This keeps old foreign holdings usable without adding deposits.
No historical snapshot amounts, metadata or exchange rates are modified.

A downgrade backfills the old scalar fields for representable single-currency
presets. It refuses to run when multi-currency rows, nonzero extra payments or
non-viewing loan destinations would lose information. Migration tests seed the
previous schema, upgrade and verify preserved values, check a reversible downgrade,
and verify rejection/rollback of a lossy downgrade. Live migration requires the usual
ignored database backup first.

## Draft screen

The existing uncommitted modelling screen is adapted to the new contract. Group
selections and successful snapshot switches reconcile retained selected groups
against copied household Asset items, adding missing native currency settings
with zero contributions; existing
contributions are retained. The user can add pots for future contributions even
without corresponding current holdings. Viewing currency is separate and does not
change configured pot amounts or loan destinations. Used loan destination pots
cannot be removed until their loans point elsewhere.

Each selected loan displays regular/extra payment inputs in native currency and a
configured investment-destination selector. Preset saving/loading controls remain
the following UI slice. No scenario is automatically persisted.

## Verification completed

- Solution build: 0 warnings/errors; EF reports no pending model changes.
- API suite: all 322 tests passed, including mixed-currency HTTP flows and invalid
  currency/extra-payment/destination payloads.
- Final data suite: all 490 tests passed, including historical foreign-currency
  migration backfill, single-currency preservation, downgrade protection,
  destination reassignment, independent native growth and viewing invariance.
- Frontend suite: all 85 tests passed; production builds passed.
- Chromium: simultaneous GBP/ZAR inputs, regular plus extra mortgage request values,
  destination retention when changing view, graph/hover, money formatting and a
  430px layout without page overflow all passed.

The local app was rebuilt and the preset migration applied after an ignored private
database backup. Local presets were empty; the final historical backfill refinement
therefore had no local preset rows to repair. Migration tests verify that refinement
against a populated previous-schema fixture, including a holding now excluded from
live setup but included in a historical snapshot. Historical financial snapshots,
metadata and exchange-rate rows are unchanged. Screenshots and database dumps stay
outside version control. No commits or pushes were performed for this slice.

Review additions: an isolated single-pot/nonzero-extra-payment downgrade test
checks rejection, unchanged migration history and preserved loan settings. A
historical snapshot-switch UI regression checks automatic native currency setup,
retained contributions/returns, and no inference from excluded/unselected holdings.

Follow-up review validation: all 4 currency migration cases and all 85 frontend
tests passed. Production builds/whitespace checks passed, and the local frontend
was rebuilt. Full unrelated backend suites and live browser checks were not
repeated for this test/UI-helper-only follow-up.
