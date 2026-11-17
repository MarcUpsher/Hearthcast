# Budget setup services

17 November 2026. Adds BudgetSetupService over the existing
[budget domain/persistence](budget-domain-slice.md). This slice owns reusable
outgoing/allowance defaults. Snapshot capture/carry-forward/correction services,
as-of/current-outgoing reads, API endpoints and UI remain subsequent work.

## Operations

| Operation | Behaviour |
| --- | --- |
| CreateItemAsync | Resolve a household's category and optional merchant; create a normalised, uniquely coded budget item. Currency and recurrence are required, with interval defaulting to one. |
| UpdateItemAsync | Change name, category, optional merchant, currency, frequency, interval and notes. Identity and code stay stable. Null merchant and null/blank notes clear those defaults. Retirement is unchanged. |
| SetItemRetiredAsync | Retire or restore an item without deleting it or changing its other defaults. |
| GetItemAsync | Return the item in the requested household, including retired items. Missing/foreign IDs return ResourceNotFoundException. |
| ListItemsAsync | Return that household's items ordered by name, code and UUID. includeRetired defaults to true, matching spending setup; false filters on the item's own retirement. A known empty household returns an empty list; a missing household returns ResourceNotFoundException. |

Categories and merchants remain shared Spending setup identities, managed by
SpendingSetupService. Budget setup does not create a second classification/provider
catalogue. References are resolved in household scope, with missing and foreign
references treated alike. A null merchant means none; an empty UUID is a missing
reference, not an instruction to clear it. Explicit selection of retired categories,
merchants or items remains possible for historical work. Retirement does not free
the item's code. Budget codes have their own namespace, separate from spending
catalogue codes, and can repeat across households.

The domain enforces shared code/text normalisation, the 64-character code limit,
nonblank names, supported new currency selections and valid Week/Month/Year
recurrence with a positive integer interval. An existing setup currency may be
retained during edits even if it has since left the capture list; it must still
have a normalised three-ASCII-letter code. New items and changes to another code
require current support. This extends the historical-currency preservation policy
to setup edits without weakening new selection validation.

No amount is stored or derived here. Updating setup cannot confirm or recarry an
entry, change a saved budget's metadata/amount/provenance/revision, or alter recorded
spending. Current outgoing amounts will be derived from effective snapshots by
the later snapshot service.

## Transactions and conflicts

Each write uses the existing SerializableTransaction helper and a fresh context
per attempt. Validation failures dispose the transaction without saving partial
metadata changes. Database failures roll back all edits. Cancellation propagates.
Confirmed serialization/deadlock failures reload state on retry; uncertain commit
failures are not automatically replayed.

Duplicate normalised codes, including retired items, raise
BusinessRuleViolationException. The service checks before insertion and translates
only the database's exact IX_BudgetItems_HouseholdId_Code unique violation for a
racing create. Its index name is shared with the explicit EF index mapping via
BudgetItemConfiguration.CodeIndexName; the existing database name is unchanged.
Other database errors propagate. Concurrent duplicate creates
therefore save one item and report one business conflict.

Setup items follow the existing setup concurrency convention: there is no setup
revision token or stale-form conflict check. Full metadata edits can replace
previously accepted defaults; serializable retries apply them to freshly loaded
state. Separate retirement and metadata operations preserve each other's fields.
Budget snapshot revisions and atomic descendant-source detachment belong to the
next service slice and are unaffected.

Future HTTP replacement contracts must require merchantId and notes fields while
allowing null to clear them; omitted fields must not silently clear defaults.
The API documentation must retain the setup last-writer-wins convention. These
are requirements for the future API slice, not additional endpoints in this slice.

## Verification

Service tests use disposable PostgreSQL databases. They cover normalised
create/update/get/list, stable codes, optional merchant/notes clearing, ordered and
retirement-filtered reads, household isolation, retired-reference selection,
duplicate reservation, invalid input rollback, exact code/interval boundaries,
historical currency retention, concurrent duplicate creation, concurrent
metadata/retirement retries, unrelated database failure propagation, cancellation
and unchanged saved plans/spending. All 26 new service tests pass. At the setup
slice checkpoint, the clean full backend regression run passed all 1,341 tests
(903 data/domain, 438 API), with no
failures or skips. Tracked/new file whitespace checks pass. The existing NU1900
warning remains for unavailable NuGet vulnerability metadata; no packages were added.

The regression run exposed an existing financed-asset concurrency test that
required exactly two SaveChanges arrivals, although valid serialization retries
can add another arrival. Its overlap assertion now permits retries, matching the
equivalent tracked-item test; the one-success/one-business-conflict and final
eligibility checks remain unchanged. No financed-asset runtime code was changed.

The subsequent review found no correctness defects. Its maintenance follow-up
shares the existing index name between mapping and conflict handling and records
the future HTTP requirements above. All 127 budget-filtered tests pass after that
follow-up, including migration/model consistency and concurrent duplicate handling.
No additional migration or runtime behaviour change is required; the full suite
was not rerun for this constant/documentation change.

No dependencies, schema changes, API registrations or migrations are added.
Next: budget snapshot services for canonical create retries, effective-date reads,
explicit carry/confirmation and transactional correction/source-link detachment.
