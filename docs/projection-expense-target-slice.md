# Projection expense target

Status: implemented. This is an optional indicator on the existing projection,
not a withdrawal simulation or a guarantee of sustainable retirement income.

## Settings and persistence

`assumptions.expenseTarget` is optional. Omitted or null disables the target.
When enabled, all four fields are required:

| Field | Meaning |
| --- | --- |
| `annualExpenses` | Positive annual spending in the starting snapshot's purchasing power. |
| `currencyCode` | Fixed three-letter expense currency, independent of the graph's viewing currency. |
| `annualWithdrawalRatePercent` | Positive annual rate, at most 100. The editor initially suggests 4. |
| `includeSavings` | Include the selected savings bucket as well as investments; initially false. |

Presets save assumptions, not calculated milestones. Applying the same preset to
a different starting snapshot recalculates everything from that snapshot. Changing
the viewing currency never reinterprets the annual expense amount. Disabling the
target clears its saved settings; enabling it starts a new blank amount.

The additive `AddProjectionExpenseTargets` migration adds three nullable scalar
columns and one boolean to `ProjectionPresets`. Existing presets remain disabled.
A database check enforces a completely absent or completely valid target, including
positive finite amounts, uppercase currency codes and a valid withdrawal rate.
Snapshots and calculated summaries need no new storage.

## Calculation

```text
Required starting capital = annual expenses / (withdrawal rate / 100)
Nominal target[t] = required starting capital * cumulative inflation[t]
Real target[t] = required starting capital
```

Eligible balances are the investments selected for this projection, plus savings
only when enabled. Contributions and reinvested loan payments become eligible as
they enter those balances. Financed asset values and property equity are excluded.
Unselected or household-excluded items are still excluded by existing preparation.

The existing single scenario inflation rate indexes the target in every currency.
All conversions use the starting snapshot's saved exchange rates, held fixed for
the projection. The expense currency needs a saved conversion to the viewing
currency even when it has no contribution pot. Missing rates block calculation
with the existing diagnostic; the run never fetches or invents rates.

Coverage is compared in the fixed expense currency using unrounded amounts. To
absorb tiny compounding or conversion remnants, the comparison allows a tolerance
of at most half a cent and one trillionth of the nominal required capital. The
relative bound prevents a positive target from being erased, even under long
deflation or for sub-cent targets. Output balances retain their original decimal
precision. Existing overflow/underflow limits return validation errors rather
than spurious milestones.

Every month, including month zero, returns `expenseTarget` with nominal/real required
capital, nominal/real eligible balances, and `isCovered`. The run-level
`expenseTarget` includes the original expense settings, required capital in expense
currency, the first reached date (nullable), and whether it was reached at the
start. Both properties are null when disabled or when preparation is blocked.

The first crossing is retained even if the portfolio later falls below target.
Nominal/real display changes and viewing-currency changes retain the same crossing
date. No balances, contributions, loan payments, payoff dates, growth accounting or
withdrawal cash flows change when the indicator is enabled.

## Screen

The assumptions panel contains an optional Expense target section. A result card
reports already reached, first reached on a date, or not reached within the horizon.
It also warns when a previously reached target is below target at the end.

The graph adds a solid target guide and a focusable square at the first covered
month on the eligible balance curve. Including savings adds a combined investments
and savings curve; otherwise the existing investment curve is used. Hover and
keyboard inspection show the eligible amount, required amount and coverage status.
The real-value toggle operates locally on returned values. Pending results retain
their own expense settings and currency labels until recalculation finishes.

## Verification

All 212 backend projection tests pass: 150 calculation/data tests and 62 HTTP API
tests. These include target/rate boundaries, investment-only versus optional
savings, excluded property, inflation and deflation, already met and unreachable
targets, later loss of coverage, unchanged underlying simulation, fixed foreign
expense currency, missing saved quotes, malformed requests, persistence and clearing,
invalid-update atomicity, database constraints and migration upgrade preservation.
The EF model matches its latest migration.

Review regressions also cover an empty portfolio throughout a 30-year -50%
deflation scenario in both display modes, plus empty, underfunded and sufficiently
funded portfolios against a positive sub-cent target. The existing exact-boundary
and viewing-currency invariance cases still pass with the bounded tolerance.

All 118 frontend tests and the production frontend build pass. Target cases cover
editing/saving/loading, validation, request isolation, zero portfolios, markers,
local display switching, optional savings, status messages and blocked results.

Chromium checks use responses generated by the actual calculator from generic
fixtures, served by an isolated mock API. They verify the crossing date, keyboard
tooltip, flat real target, local display toggle, unreachable status, optional
savings curve, changing withdrawal rates, fixed expense currency across GBP/ZAR
views, preset save/reload, disabling the target and a 430px layout without page
overflow. They do not access or modify the live database.

The solution build passes. Its cached NuGet restore reports NU1900 because package
vulnerability metadata could not be reached from the sandbox; compilation and tests
do not require new dependencies.

The local database was backed up in ignored `backups/` before applying the migration
and rebuilding the API/web containers. All services are healthy. Calculation-only
checks against the local saved preset confirm that enabling a target leaves every
balance, loan result and growth-breakdown amount unchanged, and changing the view
currency retains the coverage dates. Existing presets remain target-disabled and
were not modified by verification.
