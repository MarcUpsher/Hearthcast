# Financed-asset association and retirement API

The setup Edit sidebar combines name and association changes through
`PUT /api/households/{householdId}/financed-assets/{financedAssetId}/details`.
It requires `name`, `owners`, `loanHolderPersonIds`, `includeInHousehold` and
`householdAllocations`. A nonblank name and the same association rules described
below apply. All changes commit together; invalid shares, foreign people and
ineligible recipients leave the name and associations unchanged. The response is
the full asset. Purchase/loan setup, identity and retirement state are preserved.
Existing snapshots keep their copied context; future captures see the edited
name and associations. No schema migration is needed.

`PUT /api/households/{householdId}/financed-assets/{financedAssetId}/name` accepts
`{ "name": "New name" }` and returns the full asset response. It trims and requires
a nonblank name and preserves identity, retirement state, ownership, allocations,
borrowers and purchase/loan metadata. Existing snapshots retain their copied names;
future captures use the new name. Unknown request fields return 400; unknown or
foreign assets return 404. Active and retired assets can both be renamed.

This slice exposes changes to financed-asset person associations and retirement
state. It uses the existing domain services and database schema.

| Method | Route | Result |
| --- | --- | --- |
| PUT | `/api/households/{householdId}/financed-assets/{financedAssetId}/associations` | 200 full financed-asset response |
| PUT | `/api/households/{householdId}/financed-assets/{financedAssetId}/retirement` | 200 full financed-asset response |

Association replacement requires all four fields: `owners`,
`loanHolderPersonIds`, `includeInHousehold`, and `householdAllocations`.
The request replaces all three person associations atomically. Ownership must
sum to 10,000 basis points. Included household allocations must independently
sum to 10,000; excluded assets must provide an empty allocation array.
Loan holders may be empty. The same person can appear in several roles.

The retirement request requires `isRetired`. Retired assets remain in the setup
inventory, keep their associations, and are omitted from new snapshots.
Reactivation allows them to appear in later snapshots again. Existing snapshots
retain their copied metadata and associations after either operation.

Both routes are scoped to the household in the URL. Invalid request shapes and
shares return 400; unknown or foreign assets/people return 404; allocating to a
nonparticipant returns 409. Repeating a valid PUT does not add duplicate rows.
These routes do not edit purchase or loan metadata.

Focused HTTP tests use disposable PostgreSQL databases to check replacement,
history preservation, exclusion, retirement/reactivation, validation, scoping,
and unchanged state after rejected requests. They verify that an included asset's
custom household split, ownership and loan holders survive retirement and
reactivation through independent reads and a later persisted snapshot. Every
required replacement field is tested when omitted and when null. Unrelated
metadata is rejected even alongside an otherwise valid association change.
