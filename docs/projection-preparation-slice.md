# Snapshot-to-projection preparation

`ProjectionPreparationService` prepares a selected household snapshot with either
unsaved assumptions/selections (`PrepareAsync`) or a saved preset
(`PrepareSavedPresetAsync`). It returns preparation data, not a saved projection or
HTTP response. The calculator and UI remain independent consumers.

The result carries the snapshot ID/revision, normalised assumptions, a nullable
`StartingPosition`, structured issues, and a coverage list. `CanCalculate` is true
only when starting inputs are available and no blocking errors exist. On error,
no partial starting position is returned. Callers must not replace a null starting
position or missing balance with zeros. Warnings alone allow calculation.

## Scope and historical data

- Only copied `IncludeInHousehold` entries contribute. Household allocations total
  100%, so included balances are counted in full exactly once, regardless of who
  holds them or how attribution is split. Ownership percentages do not halve the
  household's asset value or debt.
- Investment/savings selections take only ordinary Asset-classified items. Financed
  assets are selected separately even if their group is selected as well.
- Every copied item and financed asset appears in coverage, with its native values,
  type, copied names/group/category, and disposition: selected, omitted, excluded,
  or invalid. Debt item values in coverage remain unsigned, with FinancialType
  identifying them as debts. Unselected debt/asset values are not silently folded
  into the projection. The model's net position covers selected entries only.
- Live item names, retirement, allocations and current values are never read.
  Groups and financed assets are matched to IDs copied into the selected snapshot.
  Saved selections absent from an older snapshot are blocked, not assumed zero.
- Selected groups without any household ordinary items are explicitly flagged.
  An excluded selected financed asset is blocked. Empty selections are allowed,
  supporting a new-money-only model with zero starting balances.

## Missing, carried and currency values

Selected unknown item balances, valuations and loan balances block preparation.
A missing/unknown loan never releases a hypothetical payment. Known zero debt is
passed explicitly to the calculator and may release its budget from month 1.
Known carried-forward values are accepted with one warning per selected entry.
Unknown available-to-withdraw values do not block: they are not additional equity,
assets or debt in this model. Available-to-withdraw is never added to a starting
balance.

Investment/savings amounts convert into the preset currency using saved rates.
Financed asset values, debt and payments remain in native currency, with a fixed
conversion factor for the calculator. Rates are derived from the snapshot base:
`targetQuote / sourceQuote`. Same-currency conversion needs no quote; a cross-rate
needs both quotes (base itself = 1). Missing conversions block selected entries
only, with one diagnostic per source currency. No current rate lookup or provider
calls occur, and snapshot rates/revisions are never modified.

Overflow and unrepresentable nonzero converted balances/rates produce validation
errors rather than losing balances. Preparation checks converted financed valuations
and debts too, while retaining their native values in calculator inputs. The
calculator independently guards their future converted balances and redirected
payment cash flows. Invalid assumption/selection shapes use the
same validation as presets. Snapshot/preset lookups enforce household scope and
propagate cancellation. Snapshot graph reads retain the existing repeatable-read
consistency guarantee; a saved preset is loaded once, then applied to the selected
snapshot independently. There is no requirement to lock their revisions together.

## Validation and scope

Database tests use disposable PostgreSQL databases with real snapshots and copied
allocations. They cover saved/unsaved requests, multiple source currencies and
cross-rates, missing values/rates/selections, excluded/omitted entries, retirement
and rename isolation, zero debt, carried values, withdrawals, household boundaries,
and applying a preset to different historical snapshots without writes.

No schema changes, live database updates, HTTP endpoints or UI are added here. The
preparation service is registered with the API container for the later HTTP slice.
