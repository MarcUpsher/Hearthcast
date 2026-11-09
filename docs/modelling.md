# Modelling: current behaviour

The Modelling page explores scenarios from saved household snapshots. It starts
with the latest snapshot and the household's default preset, if one exists.
Selecting another snapshot applies the current assumptions to that starting
position. Presets store assumptions, never balances, snapshot IDs or calculated results.

## Starting balances and selections

Choose which saved groups represent investments or savings, then select financed
assets independently. Included ordinary Asset entries count once in full; saved
household allocations affect attribution elsewhere, not the total available to
model. Unselected/household-excluded holdings and ordinary debts are not silently
included. Missing historical selections and unknown selected balances are diagnosed
rather than treated as zero. Carried readings are usable and identified.

## Contributions, returns and currencies

Each currency pot has separate monthly investment/savings contributions and annual
investment-return/savings-interest assumptions. Contribution amounts stay in their
pot's currency. Annual returns are nominal effective rates compounded monthly.
Entered contributions represent starting-date purchasing power: their nominal
amounts rise with the scenario inflation rate in every pot.

The viewing currency changes converted output, not contribution units or loan
investment destinations. All conversions use the starting snapshot's saved rates,
held fixed for the projection. Missing required quotes block calculation. No live
FX lookup, future depreciation or inflation-derived FX movement is assumed.

One scenario inflation assumption applies across currencies. The display toggle
chooses nominal values or values adjusted to the starting date; it does not add
inflation to entered returns or change the simulated cash flows. It operates locally
on the returned data. Separate country inflation assumptions are not implemented.

## Financed assets

Each selected asset has its own nominal appreciation rate, annual loan interest,
regular monthly payment, optional extra payment and destination investment pot.
Payments stay in the asset's currency and remain fixed in nominal terms. Monthly
interest uses the annual loan rate divided by 12; the payment reduces principal
after interest. A budget that does not reduce debt produces a warning.

After payoff, unused payment budget is converted into the chosen investment pot
using the same saved FX factors. Different assets can pay off independently. The
asset continues to appreciate after its debt reaches zero; a diamond marks payoff
on the equity line. Available-to-withdraw funds are not added to wealth again.
Fees and insurance are outside this principal-and-interest calculation.

## Results and optional targets

The graph shows investments, savings, financed equity and modelled total. Pointer
and keyboard inspection reveal monthly values. Payoff details and the
[growth breakdown](projection-growth-breakdown-slice.md) explain the result; the
real breakdown values changes when made and separates inflation's purchasing-power effect.

An optional [expense target](projection-expense-target-slice.md) stores annual
expenses in a fixed expense currency, a configurable withdrawal percentage and
whether savings are eligible. Expenses are in the chosen starting snapshot's money;
the nominal capital target rises with inflation and the real target stays constant.
Only selected investments, optionally savings, are eligible. Property equity is
excluded. The target line and first crossing are indicators; reaching them does not
start withdrawals or change contributions, balances or loan cash flows.

## Presets and draft protection

Presets support create, replace, save-as-new, delete and a single default per
household. Saving a new default removes the old default flag. Inflation display
preference is saved with the assumptions. Deleting a preset retains an unsaved copy
of the current draft rather than silently discarding it.

Route navigation and household switching consult the active page's leave policy:
dirty preset/snapshot drafts require confirmation and in-progress writes block the
transition. Cancelling leaves the current household and draft intact. Choosing a
new starting snapshot deliberately keeps the modelling assumptions so they can be
compared against another starting position. Requests use version checks to ignore
outdated responses. During valid recalculation, prior results keep their own dates,
currency and target labels until replacement; invalid/blocked results are not
presented as complete projections.

## Contracts and implementation

Use [native currency projection contracts](native-currency-projection-slice.md) for
the current input schema and [preset management](modelling-preset-management-slice.md)
for save/default behaviour. Backend controllers/Contracts remain the HTTP authority.
The original single-currency calculator/preparation/HTTP/screen notes are retained
as implementation history, not current request examples.

The parent page owns draft and request state. Chart geometry is pure code under
`frontend/src/app/modelling/chart/`; its chart and the result panels consume the
returned report without API requests or separate scenario copies.
