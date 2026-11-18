# Budget setup HTTP API

17 November 2026. Exposes [BudgetSetupService](budget-setup-service-slice.md)
through household-scoped controllers and explicit request/response contracts under
Hearthcast.Api/Budgets. [Snapshot capture/correction, history and current-outgoing
HTTP endpoints](budget-snapshot-api-slice.md) are now implemented. UI remains deferred.

## Routes

Prefix: `/api/households/{householdId}/budgets/items`.

| Method | Relative path | Behaviour |
| --- | --- | --- |
| POST | `/` | Create a budget item. Returns 201, the saved DTO and a Location for detail GET. |
| GET | `/` | List setup items ordered by name/code/UUID; includeRetired defaults to true. |
| GET | `/{itemId}` | Get one setup item, including retired items. |
| PUT | `/{itemId}/details` | Replace editable defaults. Returns 200 with saved DTO. |
| PUT | `/{itemId}/retirement` | Retire/restore using an explicit isRetired boolean. Returns 200. |

There is no physical-delete endpoint. Shared expense categories and merchants
continue to use `/spending/categories` and `/spending/merchants`; the budget API
does not create another classification/provider catalogue. BudgetSetupService is
registered in DI. Existing spending and financial snapshot contracts are unchanged.

## Input contracts

Create requires expenseCategoryId, code, name, currencyCode and frequency.
intervalCount is optional and defaults to one; explicit null is invalid. merchantId
and notes are optional and default to null.

```json
{
  "expenseCategoryId": "11111111-1111-1111-1111-111111111111",
  "code": "ENERGY",
  "name": "Gas and electricity",
  "merchantId": null,
  "currencyCode": "GBP",
  "frequency": "Month",
  "intervalCount": 1,
  "notes": "Variable usage"
}
```

Detail PUT requires expenseCategoryId, name, currencyCode, frequency,
intervalCount, merchantId and notes. The last two fields must be present even when
null: null merchantId removes the default merchant, and null/blank notes clears
notes. Omission returns 400 and leaves saved defaults unchanged. Code, identity
and retirement cannot be changed through this request. Retirement PUT requires
isRetired (true or false); it does not replace any defaults.

All bodies reject unknown JSON fields, including household IDs, revision tokens,
amounts and read-only identity fields. Required IDs cannot be empty UUIDs. Names
must be nonblank; item codes use the shared normalised ASCII key policy and
64-character limit. NUL characters are rejected. Currency codes normalise to three
ASCII letters and new selections require the shared supported currency policy.
An already-saved historical currency can be retained when editing that item, but
cannot be newly selected on another item or changed to another unsupported code.

Frequency accepts one case-insensitive text label: Week, Month or Year. Responses
use those exact names. Numbers, numeric strings, undefined names and comma-combined
labels are invalid. The converter is local to these contracts. intervalCount is a
JSON integer from one to Int32.MaxValue; numeric strings and fractional values are
invalid. No currency, frequency or country default is inferred by the API.

## Responses and errors

BudgetItemResponse contains id, householdId, expenseCategoryId, code, name,
merchantId, currencyCode, frequency, intervalCount, notes and isRetired. These are
current reusable defaults. No amount, current outgoing or snapshot revision is
stored or returned here. Later current-outgoing responses must keep saved entry
amount/currency/recurrence/category separate from these current defaults.

Existing empty households return an empty list. Unknown households/items and
missing/foreign category or merchant references return 404 with no cross-household
updates. Empty optional merchant IDs are invalid (400), not instructions to clear
the merchant. Retired resources remain explicitly usable; includeRetired=false
filters the item's own retirement only. Codes stay reserved after retirement and
are independent of spending-item codes.

Invalid JSON/input returns 400; duplicate codes return 409, including concurrent
creates and collisions with retired items. Errors use the existing Problem Details
pipeline with trace IDs. Other database errors are not reclassified as duplicates.
Detail edits retain the existing setup last-writer-wins convention: there is no
setup revision token or stale-form protection. Snapshot revisions apply to the
later snapshot endpoints, not these setup forms. Changing defaults never rewrites
saved budget amounts, metadata, provenance or revisions.

## Verification

HTTP tests use disposable PostgreSQL databases and the actual MVC pipeline. They
cover Location/detail round-trips, named enum serialization, defaults and bounds,
retirement/filtering, explicit null versus omitted fields, last-writer-wins edits,
unknown/malformed JSON, invalid values and combined enum labels, scoped references,
duplicate/racing creates, historical currency retention and unchanged saved plans.
All 63 new HTTP cases pass. The full backend suite passes all 1,470 tests
(969 data/domain, 501 API), with no failures or skipped tests, including the snapshot
service batching follow-up and migration/model checks. Tracked and new file
whitespace checks pass. The existing NU1900 warning remains for unavailable NuGet
vulnerability metadata; no packages were added.

No dependencies or schema changes are added. No application database migration or
deployment is performed. See the subsequent [snapshot HTTP slice](budget-snapshot-api-slice.md).
