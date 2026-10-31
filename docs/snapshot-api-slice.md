# Snapshot creation and read API

- POST `/api/households/{householdId}/snapshots` creates a fresh capture and returns 201 with a GET Location.
- GET the same collection returns lightweight entries, newest date first, then ID.
- GET `.../snapshots/{snapshotId}` returns the complete saved capture.

Creation requires `snapshotDate` as an ISO date. `datePrecision` defaults to Day
(1); Month (2) normalizes the saved date to the month's first day. Optional
`values` entries contain `trackedItemId` and nullable `amount`.
`financedAssetValues` entries contain `financedAssetId`, `balance` (1 valuation,
2 outstanding loan, 3 available to withdraw), and nullable `amount`.
Amounts are nonnegative; zero is confirmed, null/omitted amounts are unrecorded.
Omitting values does not carry them forward. All active items and assets are
captured, including excluded household items. Retired entries are omitted.

The response contains copied household and person metadata, classifications,
item values, asset balances, provenance and person associations. Associations
reference the snapshot's person IDs. Collections have deterministic ID ordering.
No live EF navigation graphs are serialized. Inventory entries contain ID, date,
precision and revision, without loading all item data.

Missing households or saved snapshots return 404. Invalid inputs, duplicates,
foreign/retired item references, and unsupported withdrawal balances return 400.
The existing transaction protects capture atomicity. Saved reads use a repeatable
read transaction across the split queries. Carry-forward and corrections remain
separate service operations for a later HTTP slice. No schema migration is needed.

HTTP integration tests isolate invalid nested values using real active item/asset
IDs: negative amounts, missing/null IDs, missing/null/invalid balance kinds, and
withdrawals without a facility. Rejections leave no snapshot, person, item or
asset capture rows. Inventory tests include multiple dates, a same-date tie and
a separate household to verify ordering and scope. Existing domain tests retain
exact exception assertions, including the specific API-facing exception types.
