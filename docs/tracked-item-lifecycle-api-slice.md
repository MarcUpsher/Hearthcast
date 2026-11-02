# Tracked-item allocation and retirement API

Renaming is also available through `PUT /api/households/{householdId}/tracked-items/{trackedItemId}/name`
with `{ "name": "New name" }`. It trims and requires a nonblank name, returns the
full item response, and preserves the ID, all other setup fields and saved snapshot
labels. It works for active or retired items. Future captures use the new name.
Unknown fields return 400; unknown or foreign items return 404. No schema change.

Optional institution/platform and fund names are available on tracked items.
The frontend's Edit sidebar uses
`PUT /api/households/{householdId}/tracked-items/{trackedItemId}/details` with
`{ "name": "Retirement", "institutionName": "Example platform", "fundName": null, "includeInHousehold": true, "allocations": null }`
to replace the name, metadata and household allocation atomically. The name,
both metadata keys and `includeInHousehold` are required; the name must be nonblank,
and metadata values may be null. Allocation validation and omitted/null defaults
match the existing allocation endpoint described below. The sidebar sends explicit
shares. The response is the full item, retaining its other setup fields. Rejected edits
leave all fields unchanged. Active and retired items are editable. Existing
snapshots retain their copied details, and future captures use the new values.
No additional schema migration is needed for the combined edit endpoint.

Creation accepts `institutionName` and `fundName`. Replace both through
`PUT /api/households/{householdId}/tracked-items/{trackedItemId}/metadata` with
`{ "institutionName": "Example platform", "fundName": "Equity fund" }`.
Both keys are required for replacement; either value may be null. Blank values
clear the field, and nonblank values are trimmed. The endpoint works for active
and retired items and returns the full item response. Missing keys, unknown
properties and invalid text return 400; unknown or foreign items return 404.

Metadata does not change identity, classification, ownership, allocations or
amounts. New captures copy the current metadata, including when a balance is
carried forward. Existing snapshots and value corrections retain their saved
metadata. Snapshot responses and all three summary views expose those saved
fields. `AddTrackedItemMetadata` adds four nullable text columns; it does not
infer or backfill information from historical names.

This slice lets an existing tracked item change its household inclusion and shares,
or be retired/reactivated. Item identity, holder, group, currency, country and name
are preserved by allocation and retirement updates. There is no migration.

## Routes and contracts

Both routes are under `/api/households/{householdId}/tracked-items/{trackedItemId}`:

| Method | Suffix | Body | Success |
| --- | --- | --- | --- |
| PUT | `/household-allocation` | required `includeInHousehold`, optional `allocations` | 200 full TrackedItemResponse |
| PUT | `/retirement` | required `isRetired` | 200 full TrackedItemResponse |

The allocation endpoint replaces the entire inclusion/share configuration. It is
not a partial edit. For an included item, omitted or null `allocations` resets
shares to 100% of the holder; it does not preserve a previous custom split. Explicit
shares must be unique, positive and total exactly 10,000 basis points. An explicit
empty array is invalid for included items. For excluded items, omitted/null/empty
allocations mean no shares; nonempty shares are rejected.

Creation and replacement use the same request validation helper, and domain rules
continue to enforce eligibility. A nonparticipating holder cannot receive default
household shares; use explicit shares assigned to participants or exclude the item.
The holder is independent of household shares and remains unchanged by replacement.

Retirement changes only `isRetired`. It preserves inclusion and saved shares, and
its response includes those shares. Retired items remain visible in the setup
inventory but are omitted from new snapshots. Reactivation reuses the same item ID
and brings the item back into future snapshots. Allocation replacement is allowed
while retired. No values are inferred for periods when an item was absent.

Both PUTs are idempotent. Missing/null boolean fields, malformed JSON, unknown
properties and invalid shares return 400; missing/wrong-household resources return
404; ineligible recipients return 409. Failed replacements leave persisted state
unchanged. Household checks are scoping, not authentication or authorization.

Both service writes use serializable transactions with the existing retry helper.
The retirement operation loads allocation rows too, so returning a retired item
cannot accidentally report an empty allocation list. No HTTP revision precondition
is added to setup writes: concurrent replacements may overwrite each other after
serializable retries. This follows the existing household/category setup contract.

## Snapshot history and tests

Earlier snapshots retain their recorded inclusion and copied allocation shares.
Later snapshots see the current configuration. Retirement never removes historical
entries, and reactivation does not modify them.

HTTP integration tests cover sharing, exclusion, default restoration, retirement,
reactivation, repeated PUTs, invalid input and named validation errors, household
scoping, foreign/ineligible recipients, and rollback after rejected requests. The
full lifecycle test records snapshots before and after changes, then checks saved
history, retirement omission and reactivation using the same identity.
Focused regression tests also retire/reactivate an item with a 25/75 split and
verify responses, subsequent reads and persisted shares. Separate omitted/null
replacement cases start from a custom split and verify that it is removed and
replaced by the holder's 100% default.

```sh
docker compose --profile test run --build --rm tests
```

Latest verification: **105 HTTP integration cases pass**, including 12 lifecycle
cases added by this slice. The unchanged database/service suite passed all 385 cases
in the preceding full run. Both test-project builds have zero warnings and errors.

The running API has not been upgraded by this slice. Changes are uncommitted for
review.
