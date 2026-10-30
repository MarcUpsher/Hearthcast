# Snapshot lifecycle: carry-forward and corrections

Implemented locally, 30 October 2026. This completes the second ordinary-snapshot
review point. The [household history specification](household-history-tests.md)
records the agreed synthetic data, expected behaviour and future financed-asset
cases. Existing setup, capture and PostgreSQL constraint tests remain in place.

## Files to review

| File | Responsibility |
| --- | --- |
| [SnapshotService.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotService.cs) | Fresh/next capture, source selection, corrections and consistent reload. |
| [Snapshot.cs](../backend/src/Hearthcast.Data/Snapshots/Snapshot.cs) | Copied capture graph, bulk correction validation, revision and edit time. |
| [ItemSnapshot.cs](../backend/src/Hearthcast.Data/Snapshots/ItemSnapshot.cs) | Confirm/missing/carry state, independent copied amount and provenance. |
| [SnapshotPeriod.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotPeriod.cs) | Day/month periods for normalisation and date-order decisions. |
| [SnapshotConflictException.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotConflictException.cs) | Stale correction requires reload. |
| [SnapshotConfiguration.cs](../backend/src/Hearthcast.Data/Persistence/Configurations/SnapshotConfiguration.cs) | Snapshot revision concurrency token and initial database default. |
| [ItemSnapshotConfiguration.cs](../backend/src/Hearthcast.Data/Persistence/Configurations/ItemSnapshotConfiguration.cs) | Carry-state checks and same-item/currency source FK. |
| [HouseholdHistoryTestData.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/HouseholdHistoryTestData.cs) | Synthetic household, classifications and evolving item setup. |
| [HouseholdHistoryTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/HouseholdHistoryTests.cs) | Complete 2015–2030 lifecycle. |
| [SnapshotCarryForwardTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/SnapshotCarryForwardTests.cs) | Date selection, confirmations, clears, identities, currency and provenance. |
| [SnapshotCorrectionTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/SnapshotCorrectionTests.cs) | Corrections, stale/concurrent writes, history independence and rollback. |
| [SnapshotCarryIntegrityTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/SnapshotCarryIntegrityTests.cs) | Database constraints, allocation copying and migration upgrade/downgrade. |
| [SnapshotConcurrencyTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/SnapshotConcurrencyTests.cs) | Coherent reads during corrections and EF concurrency enforcement. |

## Service behaviour

CreateAsync retains its original behaviour: supplied amounts are Confirmed,
supplied nulls and omitted items are Unrecorded. It never carries implicitly.

CreateNextAsync builds and saves a new capture from one selected source snapshot.
It uses current active setup for the roster, metadata, holder, inclusion and
allocations. Supplied amounts confirm values as of the new financial date;
explicit null clears a value; omitted inputs carry the selected source entry.
New items are missing. Retired items are omitted. Names do not identify items.

This is a persisted service operation, not a draft form or HTTP endpoint.
Consumers can choose a source explicitly through sourceSnapshotId. A source from
another household or a definitely future period is rejected. With no explicit
source, the service selects the unambiguously latest preceding capture. If no
preceding capture exists, it starts with missing values.

For ordering, Day is a single day and Month spans its calendar month, including
leap days. Financial storage still normalises Month to its first day. Overlapping
periods or tied most-recent captures require explicit selection. Entry timestamps
and arbitrary IDs do not break financial-date ties. An explicit overlapping-period
choice acknowledges the ordering uncertainty; a known future confirmation still
cannot be carried backwards. Later unambiguous dates supersede older ambiguity.

Only that selected snapshot supplies balances. Missing values remain missing;
there is no backward search to revive an older balance. A known carry, including
zero, copies the amount, original confirmation date/precision and immediate source
entry ID. Unknown confirmation metadata remains unknown. Changing currency requires
a supplied fresh value or an explicit clear. Country alone does not change currency.

CorrectValuesAsync takes household ID, snapshot ID, expected Revision and a
nonempty collection of ItemSnapshotValue inputs. It updates only existing saved
entries, including retired items. A supplied amount, even unchanged, confirms the
entry at the saved snapshot's financial date/precision; null clears its value and
confirmation/source metadata. It retains all copied reporting context and dates.

Successful corrections change Revision and UpdatedAtUtc. CreatedAtUtc is retained.
Later saved copies are unchanged, even if a source is corrected or cleared. Their
source ID is provenance for the copying operation, not a live formula or a promise
that the source's current amount remains identical.

## Consistency and database rules

Revision is a GUID concurrency token for the whole capture. A caller supplies the
revision it read; a stale edit raises SnapshotConflictException and must reload.
EF also checks the original revision in the UPDATE predicate. This follows EF's
[application-managed concurrency token pattern](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
It does not introduce historical item versions or silently merge concurrent edits.

Creation and correction use the existing short serializable transaction/retry
helper. A retry reloads state and rechecks the expected revision. Split-query
GetAsync reads use a short RepeatableRead transaction, so the header revision and
entries come from one database view during concurrent corrections.

The new migration AddSnapshotCarryForwardAndCorrections adds Snapshot.Revision,
ItemSnapshot.CarriedFromItemSnapshotId, an alternate key and source FK. Existing
captures receive independent GUID revisions. Previous baseline migration files
are unchanged. The source FK includes tracked-item identity and currency; a row
cannot refer to itself. State checks require the appropriate amount, origin,
source and paired confirmation fields. Source chronology is an application rule.

The incremental upgrade preserves confirmed/missing captures and their metadata.
A downgrade with existing carried values fails the older value-state constraint;
PostgreSQL rolls the migration back, preserving the data and provenance. It does
not silently relabel carried values as confirmed. Downgrade/reapply with compatible
values and full-chain rollback/reapply on empty test data are verified.

## Verification and limits

Builds pass with zero warnings/errors. All 201 PostgreSQL integration cases pass
inside Docker: the preceding 141 plus 56 lifecycle cases and four retry-boundary cases. Tests create and remove
isolated databases; no demo data or application household has been created.

The suite includes the multi-currency household history, missing/zero/exact values,
retirement/replacement, copied labels and allocations, confirmation chains,
month/day/same-date ambiguity, backdating, explicit source validation, currency
changes, stale and concurrent edits, partial-failure rollback, consistent reads,
physical constraints and migration preservation.

Review follow-up: household participation, allocation/participation and correction
races now gate both initial operations immediately before SaveChanges. Both finish
their reads and validation before either writes; retries are not gated. This makes
the tests exercise overlapping database views rather than relying on task timing.
The shared test factory and save interceptor do not alter production services.

Retry-boundary tests fail at allocation insertion, after earlier capture rows have
been inserted. For both serialization failures and deadlocks, they verify exactly
five failed attempts and no surviving capture rows. Cancellation immediately after
the first aborted save prevents retry backoff from continuing and leaves no partial
capture. The tests verify the propagated cancellation token and one attempt.
Npgsql wraps exhausted transient save failures in InvalidOperationException;
the underlying PostgreSQL error code is checked as well.

Financed assets, calculated summaries, historical metadata templates, imports,
finance HTTP endpoints and the frontend remain separate slices. The reference
household totals are hand-calculated expectations for the later reporting slice.

Only the test image was rebuilt and executed. The running application database
and API container remain at the pushed PostgreSQL checkpoint. After reviewing
this local slice, rebuilding the stack with `docker compose up -d --build --wait`
will apply the incremental migration through the migration service.
