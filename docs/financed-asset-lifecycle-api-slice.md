# Financed-asset association and retirement API

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
