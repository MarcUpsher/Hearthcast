# Spending setup API

17 November 2026. Exposes the existing [setup services](spending-setup-service-slice.md)
through controllers and explicit wire contracts. No schema change, spending-record
capture, budgets or UI are included. Tests use disposable PostgreSQL databases;
the running app and database have not been deployed/migrated for this slice.

## Routes

All paths below start with `/api/households/{householdId}/spending` and use UUID
identifiers. Expense categories are independent of asset/debt categories.

| Method | Path | Behaviour |
| --- | --- | --- |
| POST | `/categories` | Create an expense category from `{ name }`. |
| GET | `/categories` | List categories; optional `includeRetired` defaults to true. |
| GET | `/categories/{categoryId}` | Read one category. |
| PUT | `/categories/{categoryId}/name` | Rename using `{ name }`. |
| PUT | `/categories/{categoryId}/retirement` | Set `{ isRetired }`. |
| POST | `/merchants` | Create a merchant from `{ name }`. |
| GET | `/merchants` | List merchants; optional `includeRetired` defaults to true. |
| GET | `/merchants/{merchantId}` | Read one merchant. |
| PUT | `/merchants/{merchantId}/name` | Rename using `{ name }`. |
| PUT | `/merchants/{merchantId}/retirement` | Set `{ isRetired }`. |
| POST | `/items` | Create an item with explicit category, code, name and unit. |
| GET | `/items` | List items; optional `includeRetired` defaults to true. |
| GET | `/items/{itemId}` | Read one item. |
| PUT | `/items/{itemId}/details` | Replace mutable metadata. |
| PUT | `/items/{itemId}/retirement` | Set `{ isRetired }`. |
| GET | `/items/{itemId}/usual-prices` | List an item's remembered prices. |
| GET | `/items/{itemId}/usual-price?merchantId=...&currencyCode=GBP` | Exact price lookup. |
| POST | `/usual-prices` | Create an explicitly remembered price. |
| GET | `/usual-prices/{priceId}` | Read one remembered price. |
| PUT | `/usual-prices/{priceId}` | Update/reconfirm with an expected revision. |

Creates return 201 with a Location for GET and the saved response. Reads and
successful mutations return 200. Exact lookup returns 204 with an empty body when
the item and merchant exist in the household but no price matches that currency
and unit. It never falls back to another shop, currency or pack. Missing/foreign
items or merchants return 404 instead. Lists of existing empty collections return
200 with `[]`; unknown parents return 404. No delete endpoints are offered.

## Request contracts

Item create:

```json
{
  "expenseCategoryId": "11111111-1111-1111-1111-111111111111",
  "code": "EGGS-6",
  "name": "Eggs",
  "unitKey": "pack-6",
  "unitLabel": "Six eggs",
  "notes": null
}
```

Item details update requires `expenseCategoryId`, `name`, `unitLabel` and an
explicit `notes` member (null clears it). Create allows notes to be omitted.
Code and UnitKey cannot be changed; supplying those, retirement or other unknown
JSON members in an update is a 400 error rather than a silently ignored edit.

Price create requires `spendingItemId`, `merchantId`, `currencyCode` and
`usualUnitPrice`. Price update requires `expectedRevision` and `usualUnitPrice`.
Zero prices and explicit confirmation of an unchanged price are valid. An update
cannot change the price identity's item, merchant, currency or unit.

Required values use nullable request fields so missing booleans, decimals and
identifiers cannot silently become false, zero or an empty UUID. Request validation
rejects missing/null/empty identifiers, invalid currency shape and null characters
in descriptive text. Domain validation remains authoritative for canonical
codes/units, trimmed length bounds and exact price limits/precision. This avoids
duplicating domain normalisation rules in the HTTP layer. Model binding rejects
malformed JSON, invalid value types and out-of-range decimal representations.

Currency support remains staged: this setup API normalizes three-letter codes but
does not validate currency availability, fetch rates or apply receipt minor-unit
rounding. The [record domain](spending-records-slice.md) now supplies an explicit
supported-currency/receipt policy; record HTTP capture remains planned.

## Responses, errors and editing semantics

Category/merchant responses contain `id`, `householdId`, `name` and `isRetired`.
Item responses also contain `expenseCategoryId`, normalized `code`, `unitKey`,
`unitLabel` and nullable `notes`. Price responses contain their identity IDs,
currency, copied unit key/label, exact decimal `usualUnitPrice`, `updatedAtUtc` and
`revision`. Timestamps are returned at PostgreSQL's microsecond precision so
create/update and later GET return the same representation. Amounts are JSON numbers.

Errors use Problem Details with `traceId`: 400 for invalid input, 404 for missing or
foreign-household resources, and 409 for reserved/duplicate item codes, duplicate
price combinations or stale usual-price revisions. Required-field/model-binding
errors include named field errors. Domain validation can return the existing
generic invalid-request detail. Stale price responses have title
`Usual price revision conflict`; reload before retrying with a new revision.

Inventory order and retirement semantics are inherited from the service layer.
Active-only filtering checks the listed entity's own retirement flag; a retired
category does not implicitly hide its active items. Explicit references to retired
setup remain valid for historical use. Category/merchant/item metadata updates
remain last-writer-wins; transaction isolation is not stale-form detection. Usual
price updates always require their revision, including after transaction retry.

## Verification

All 52 new HTTP integration tests pass. They exercise the complete workflow, Location round-trips,
normalisation and exact decimal responses, immutable fields, validation and
nonmutating failures, missing/foreign references, retired-code reservations,
retirement filtering/restoration, 204 versus 404 price lookup and stale-price 409s.
Service tests retain coverage for simultaneous creates and competing price edits.

Full backend verification passes: 1,052 tests (666 data/domain and 386 HTTP API),
with no failures or skipped tests. The run builds all backend projects and includes
migration/model checks. The existing NU1900 warning remains because NuGet
vulnerability metadata is unavailable; no package dependencies were added.

Spending-record [domain/persistence](spending-records-slice.md) is implemented.
[Record services](spending-record-service-slice.md) are also implemented.
[Record HTTP capture](spending-record-api-slice.md) is also implemented. Budget
domain/persistence follows; UI remains deferred.
