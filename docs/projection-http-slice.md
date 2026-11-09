# Projection HTTP API

> Historical implementation record. Start with the [current modelling guide](modelling.md)
> and [native currency contracts](native-currency-projection-slice.md). The original
> single-currency or initial-screen details below include superseded behaviour;
> retain them for design history, not as current payload examples.

All routes are household scoped and use the existing stateless controller and
Problem Details conventions. This slice adds no authentication, schema changes,
UI, live database migration or saved projection results.

## Presets

Base: `/api/households/{householdId}/projection-presets`

Create/update also accept `isDefault` (omitted means false); responses include it.
Saving true replaces this household's previous default atomically. See
[preset management](modelling-preset-management-slice.md) for the current behaviour.

- `GET /`: ordered preset list, 200 (empty list allowed for an existing household).
- `GET /{presetId}`: preset with inputs and UTC timestamps, 200.
- `POST /`: create, 201 with Location pointing to GET.
- `PUT /{presetId}`: full replacement of name, assumptions and selections, 200.
  Omitted children are removed, rather than appended to existing selections.
- `DELETE /{presetId}`: permanent preset deletion, 204; source data is not deleted.

Example create/update body:

```json
{
  "name": "Baseline",
  "inputs": {
    "assumptions": {
      "currencyCode": "GBP",
      "durationYears": 10,
      "monthlyInvestmentContribution": 500,
      "annualInvestmentReturnPercent": 5,
      "monthlySavingsContribution": 100,
      "annualSavingsInterestPercent": 2,
      "annualInflationPercent": 3,
      "showInflationAdjustedValues": false
    },
    "groups": [
      { "groupId": "11111111-1111-1111-1111-111111111111", "bucket": "Investment" }
    ],
    "financedAssets": [
      {
        "financedAssetId": "22222222-2222-2222-2222-222222222222",
        "monthlyLoanPaymentInAssetCurrency": 15000,
        "annualLoanInterestPercent": 9,
        "annualAssetAppreciationPercent": 2
      }
    ]
  }
}
```

Values above are illustrative payloads, not default forecasts. Every assumption,
including boolean/zero values, and both selection arrays are required. Arrays may
be explicitly empty. Preset responses contain `id`, `householdId`, `name`, `inputs`,
`createdAtUtc`, `updatedAtUtc`; the returned inputs have the same shape as the write
inputs. There is no saved snapshot reference.

Buckets accept strings `Investment`/`Savings` (case-insensitive on input), not
numeric enum values. Other code fields in projection responses are also readable
strings: issue kind/severity, coverage kind/disposition/financial type, warning kind.
Amounts and rates remain JSON numbers. Loan payment amounts remain in each asset's
native currency; investment/savings contributions use each configured pot's currency.

## Run without saving

Assumptions can include an optional `expenseTarget`; both monthly output and the
run-level response then include coverage results. See the
[expense target slice](projection-expense-target-slice.md) for the contract,
persistence, currency and inflation rules. Existing bodies without a target
retain their behaviour.

Projection run responses also include `growthBreakdown`, containing `nominal` and
`inflationAdjusted` explanations of the complete horizon. It is null for blocked
runs. See [Projection growth breakdown](projection-growth-breakdown-slice.md) for
the fields, inflation accounting and reconciliation rules. The growth breakdown
adds no request settings or persisted calculation results.

`POST /api/households/{householdId}/snapshots/{snapshotId}/projection`

Supply exactly one mode:

```json
{ "presetId": "33333333-3333-3333-3333-333333333333" }
```

or `{ "inputs": { ... } }` using the same inputs shape as above. Unsaved inputs
need no preset name. Both modes prepare the selected snapshot, then invoke the pure
calculator only when preparation permits it. A saved preset is loaded once for that
run; it is not updated, and no implicit latest-snapshot selection occurs.

The 200 response contains:

- `snapshotId`, `snapshotRevision`, `currencyCode`.
- `canCalculate`.
- `months`: month 0 and every projected month, or null when preparation is blocked.
  Each month contains nominal/inflation-adjusted totals, native per-asset data,
  payoff dates and redirected amounts. Monetary display rounding is the client's job.
- `issues`: preparation warnings and blocking data issues, with source IDs/messages.
- `coverage`: every copied snapshot entry, showing selected/omitted/excluded scope.
  Values are native amounts; a Debt item's unsigned value is labelled by its type.
- `warnings`: calculation warnings such as a payment not reducing loan debt; empty
  if preparation prevented the calculator from running.

Missing snapshot values, selections or rates are 200 with `canCalculate: false`,
`months: null` and blocking diagnostics, not a partial/zero-filled projection.
Carried-value warnings allow calculation. Coverage makes omitted holdings/debts
visible: projected net position covers only the selected model inputs, not necessarily
all household wealth.

Run requests do not write presets, snapshots or exchange rates and never fetch
rates. Both complete output series are returned regardless of the display toggle;
the UI can switch inflation adjustment without recomputation. Cancellation tokens
are propagated through reads and checked before/after the synchronous calculator.

## Errors and validation

- 400 Problem Details: malformed JSON, missing required fields, unknown properties
  (including nested input objects), null/duplicate selection entries, empty IDs,
  unknown/numeric buckets, invalid assumptions or unsupported numeric results.
- 404: missing household/preset/snapshot or attempting to access another household's
  resources. Preset writes also reject foreign/missing group or asset references.
- 409: duplicate preset name within the household (case-sensitive after trimming).

Run selections absent from a snapshot produce data diagnostics rather than looking
up another household's live entity. The preparation adapter never uses live setup
to reinterpret historical snapshots. API responses use DTOs/immutable calculation
records, never EF entity graphs. Existing generic exception handling keeps internal
errors out of public responses.

## Verification

HTTP integration tests use the real application in Production test configuration
and disposable PostgreSQL databases. They cover preset CRUD/Location, replacement,
normalisation, saved/unsaved equivalence, payoff reinvestment, no-write/no-provider
behaviour, missing data, warnings, household boundaries, conflicts, invalid nested
payloads and numeric limits. Existing API tests are included in regression checks.

Validation for this slice: solution build passed with 0 warnings/errors. All 313
API integration tests passed (42 new projection cases plus 271 existing cases),
using the local configured PostgreSQL helper and disposable test databases:

```sh
dotnet build Hearthcast.slnx --no-restore --disable-build-servers -m:1
python /tmp/hearthcast-run-local.py dotnet test backend/tests/Hearthcast.Api.Tests --no-build --no-restore
git diff --check
```

The separate data-layer and frontend suites were not rerun for this API-only change.
