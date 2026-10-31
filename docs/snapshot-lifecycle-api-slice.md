# Snapshot carry-forward and correction HTTP API

POST `/api/households/{householdId}/snapshots/next` takes the same date,
precision and value arrays as fresh creation, plus optional `sourceSnapshotId`.
It returns 201 and a GET Location. Without an explicit source it selects the
latest unambiguous earlier snapshot. Ambiguous ordering returns 409; selecting
a definitely future source returns 400; missing or foreign sources return 404.
With no prior history, it creates an unrecorded capture from active setup.
New items appear unrecorded; retired items are omitted. Changed currencies require
fresh values or explicit clearing of the incompatible values.

PATCH `/api/households/{householdId}/snapshots/{snapshotId}/values` requires
`expectedRevision` and at least one item value or asset balance. It returns 200
with the complete corrected snapshot and a new revision. A stale revision returns
409; the client must reload before deciding what to change. Invalid saved-entry
references return 400; missing or foreign snapshots return 404.

An omitted entry leaves its value alone (or carries it when creating next).
A supplied entry with a null or omitted amount clears that value to unknown.
Zero is a confirmed value. For financed assets these rules apply per balance.
Carry-forward keeps the original confirmation date and source entry reference.
Corrections update confirmation metadata for changed entries only. They do not
rewrite other snapshots or refresh the saved setup metadata. Transactions make
mixed item and financed-asset corrections atomic.

Validation is shared with fresh capture. Unknown JSON members, invalid amounts,
duplicate entries, null entries, and missing IDs are rejected with 400. Only
specific expected domain exceptions map to public 400/404/409 responses.

HTTP tests isolate missing/null/empty revisions from empty corrections using
otherwise valid saved data. PATCH clearing is checked for both an item and a
carried asset balance, including removal of confirmation/source metadata,
preservation of omitted entries and balances, and persisted GET results.
