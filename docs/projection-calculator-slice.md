# Monthly projection calculator

`ProjectionCalculator.Calculate(assumptions, startingPosition)` is a pure function.
It takes unsaved or saved assumptions plus prepared, known starting balances. It
has no database access, HTTP calls, snapshot lookup or writes. The snapshot adapter
and interactive screen are subsequent slices.

## Inputs and scope

Investment and savings starting balances/contributions use the preset currency.
Each financed asset supplies its native currency, known valuation/debt, its loan
assumptions and a fixed conversion rate: projection currency per one native unit.
A same-currency rate must be 1; other rates must be positive. Identifiers must be
nonempty/distinct. Missing/unknown values or rates must be rejected by the future
adapter, never defaulted to zero or treated as paid off. No assets means no loans;
this is different from a supplied asset with a confirmed zero debt.

Balances must be nonnegative; negative equity is supported when debt exceeds asset
value. Shared assumption validation uses the same bounds as preset persistence.
A preset name is not needed to calculate an unsaved scenario.

The caller supplies the intended household scope/allocation. Totals cover only the
supplied investment, savings and financed-asset balances. They are modelled net
position, not automatically the full household net worth if other holdings or debts
were omitted. The snapshot adapter must make this coverage explicit.

## Monthly calculation order

Month 0 retains the starting values without payments, growth or contributions.
Future dates use `startingDate.AddMonths(month)`, preserving the original day where
possible rather than drifting after February.

1. Grow investment/savings/asset balances over the month. Their annual rates are
   effective annual growth before inflation: monthly factor = `(1 + rate/100)^(1/12)`.
2. Charge each loan interest on its opening debt. Loan annual interest is a nominal
   annual rate with monthly rate `rate/1200` (no daily accrual or banking fees).
3. Apply up to the monthly payment budget against debt plus interest. Debt is never
   reduced below zero. Any budget left over goes into investments at month-end.
4. Add the regular investment/savings contributions at month-end. They and redirected
   loan payments begin earning returns in the next month. Contributions and payment
   budgets stay fixed in nominal terms throughout the projection.

Every loan pays off independently. In the payoff month the remainder is redirected;
subsequent months redirect its full budget. A confirmed zero starting debt redirects
from month 1, with payoff date equal to the start date. A missing asset has no such
assumed budget. Loan payoff dates are estimated monthly dates relative to the starting date.

A payment at or below interest produces one structured warning per asset, with the
first affected month. The projection continues with the unpaid debt/interest, and
no fictitious payoff is generated. Zero-interest zero-payment loans also warn if a
balance remains. Loans not paid off by the horizon retain a null payoff date.

Native asset/debt balances and redirected cash flows use the supplied fixed rate
for aggregate results. No exchange-rate forecasts are introduced. Financed equity
is value minus outstanding debt; available-to-withdraw balances are not additional
assets or a second equity contribution.

## Results and numeric behaviour

Results include month 0 and every future month, combined nominal balances, combined
inflation-adjusted balances, native per-asset balances/interest/payments, payoff
dates and redirected amounts. Net position = investments + savings + financed equity.

Inflation-adjusted balances divide nominal amounts by
`(1 + annualInflation/100)^(month/12)`, using a compounded monthly factor. They express
purchasing power at the selected starting date. The UI toggle chooses which series
to display; it does not alter payments or calculation behaviour.

Amounts use decimal arithmetic without monthly cent rounding. Monthly growth factors
use `Math.Pow` converted to decimal, so tests allow a small tolerance for compounding.
The positive annual factor is computed in decimal before conversion to double,
preserving valid rates close to -100%. If a positive balance or inflation factor
rounds to zero while compounding, calculation fails with the numeric-limits
validation error rather than silently wiping that balance.
The calculator is an illustration, not an exact bank amortisation statement.
Unsupported date ranges and numeric overflow/underflow are reported as validation
errors rather than returning incomplete results. Returned collections are read-only;
calculation does not mutate starting inputs.

No migration or API endpoints are added in this slice. Unit tests run without a
database, covering growth, contributions, negative returns, multiple loans, interest,
payoff remainders, fixed conversion, inflation, warnings, immutability and validation.
