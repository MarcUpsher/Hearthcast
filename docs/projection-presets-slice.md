# Projection presets: persistence

> Updated model: see [Native currency projection pots](native-currency-projection-slice.md)
> for the current schema, request shape and per-currency calculation. The original
> single-currency slice notes below describe the preceding implementation.


A preset belongs to a household and stores reusable assumptions, group selections,
and independent financed-asset assumptions. It has no snapshot link or saved
starting balances. The future modelling screen selects its starting snapshot
separately and defaults to the latest snapshot.

## Model

- `ProjectionPreset`: name, currency, duration, monthly investment/savings
  contributions, nominal annual returns, inflation, display toggle, UTC timestamps.
- `ProjectionPresetGroup`: one group in either Investment or Savings. The composite
  primary key prevents a group appearing twice, including in different buckets.
- `ProjectionPresetFinancedAsset`: one asset with its own monthly payment, annual
  loan interest, and annual appreciation. Multiple loans are supported.

Investment and savings contributions are expressed in the preset currency.
`MonthlyLoanPaymentInAssetCurrency` is expressed in the referenced financed asset’s
native currency, which is fixed on that asset. Changing the preset currency must not
reinterpret a loan payment. The calculator will amortise each loan in its native
currency and convert results (including payments redirected into investments) using
the selected snapshot’s fixed exchange rates. Missing required rates prevent a
complete projection; they must never silently become zero.
Percentages use 5 for 5%, not 0.05. This slice stores assumptions only; compounding,
exchange-rate use, loan amortisation, and payment redirection are for the calculator
slice. Calculated results will not be persisted.

Group selection is explicit, never inferred from names. Only Asset-classified
groups are accepted for Investment/Savings; calculations will take ordinary tracked
items from these groups, with financed assets handled separately to avoid counting
them twice. Retired groups/assets remain selectable for historical modelling.
Membership/value availability in a particular snapshot is checked when a projection
is prepared, not when saving a reusable preset. Empty selections and zero payments
are permitted. A selected loan missing from a snapshot (or with an unknown balance)
is not a confirmed zero-balance loan and must not release its payment into
investments. Missing selections/values must be flagged before calculating. A loan
payment insufficient to cover interest will need a visible
warning in the modelling screen rather than preventing a scenario from being saved.

## Validation and persistence

- Nonblank trimmed name, unique within a household (case-sensitive).
- Uppercase three-letter currency code, using existing code validation.
- Duration: 1–100 years.
- Contributions/payments: nonnegative.
- Investment return, savings return, asset appreciation, inflation: greater than
  -100% and at most 100% annually. Negative inflation represents deflation.
- Loan interest: 0–100% annually.
- No implicit default assumptions are stored by the service; callers supply all
  values explicitly. UI defaults will be chosen in the screen slice.

`ProjectionPresetService` provides create, get, ordered list, full update, and
permanent delete. An update replaces the selections; omitted children are removed.
Writes use the existing serializable transaction/retry helper. Name uniqueness is
also enforced in PostgreSQL, with uniqueness conflicts translated to a domain error.
Concurrent saves are complete preset replacements, with the final successful save
winning; no scenario edit versioning is introduced in this slice.

Composite foreign keys enforce household scope for both child relationships.
Deleting a preset cascades to its children, never to groups/assets. Referenced
groups/assets and households cannot be deleted while presets reference them.
Numeric check constraints reject nonfinite PostgreSQL numeric values as well as
invalid bounds. The migration only adds three tables and their constraints/indexes.

## Scope and checks

No HTTP endpoints, calculator, or UI yet. The service is registered with the API
container for the later HTTP slice. No changes are made to existing snapshots.

Database tests apply the migrations to disposable PostgreSQL databases and cover
round-trips without snapshots, multiple loans, bucket/loan updates, empty selections,
atomic failed validation and deferred database-write failures, null selection entries,
native loan payment units across preset-currency changes, household boundaries, duplicate names/selections, invalid
assumptions, database foreign keys/checks, and deletion behaviour.
