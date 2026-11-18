# Budget snapshot HTTP API

18 November 2026. Exposes [BudgetSnapshotService](budget-snapshot-service-slice.md)
through household-scoped controllers and explicit contracts under Api/Budgets.
[Setup endpoints](budget-setup-api-slice.md) continue to edit reusable defaults.
This completes the planned budget capture/read HTTP layer; period reporting,
allocation rules, projection import and UI remain separate work.

## Routes

Prefix: `/api/households/{householdId}/budgets/snapshots`.

| Method | Relative path | Behaviour |
| --- | --- | --- |
| POST | `/` | Capture a complete dated plan with a client UUID. 201 plus Location for creation; 200 with current retained state for a matching retry. |
| GET | `/` | Bounded descending history; pageSize defaults to 25, maximum 100. before is an exclusive effective date. Returns records, nextBefore and pageSize. |
| GET | `/{snapshotId}` | Complete saved plan, including revision and saved entries. |
| GET | `/copy-source?effectiveFrom=YYYY-MM-DD` | Latest strictly earlier plan as a suggestion; 204 if none. Does not itself select a create source. |
| GET | `/effective?asOf=YYYY-MM-DD` | Effective snapshot plus its derived exclusive end date and the requested asOf date. 204 before the first plan. |
| PUT | `/{snapshotId}/header` | Revision-checked effective date/notes correction. |
| POST | `/{snapshotId}/entries` | Add one explicit entry, using the saved parent revision. |
| PUT | `/{snapshotId}/entries/{entryId}` | Explicit full entry correction or deliberate re-copy, with parent revision. |
| PUT | `/{snapshotId}/entries/{entryId}/amount` | Enter/confirm or clear an amount, preserving saved descriptive metadata. |
| PUT | `/{snapshotId}/entries/{entryId}/metadata` | Correct only descriptive context while preserving the saved value/provenance. |
| POST | `/{snapshotId}/entries/{entryId}/remove` | Revision-checked removal, returning the retained updated plan. |

`GET /api/households/{householdId}/budgets/current-outgoings` returns setup items
alongside their entry from the effective plan. It accepts asOf and includeRetired
(default true). Both as-of reads default an omitted asOf to the current UTC date,
resolved once per action using the injected TimeProvider (TimeProvider.System by
default). Request DTOs contain input data only; explicit asOf dates bypass the clock.
Clients should pass their intended household/local date explicitly. Copy-source's
effectiveFrom is required. Dates use ISO date examples above; finite budget date
bounds exclude DateOnly's PostgreSQL infinity sentinels.

There is no snapshot DELETE or void endpoint. Entry removal is an explicit POST
command with a revision body, avoiding a DELETE body while retaining snapshot
identity. All mutations return the updated complete plan with 200 except creation.

## Capture contract and modes

```json
{
  "id": "11111111-1111-1111-1111-111111111111",
  "effectiveFrom": "2026-02-01",
  "copiedFromBudgetSnapshotId": "22222222-2222-2222-2222-222222222222",
  "notes": "Updated utility amount",
  "entries": [
    {
      "budgetItemId": "33333333-3333-3333-3333-333333333333",
      "mode": "Confirm",
      "plannedAmount": 20,
      "confirmedOn": "2026-01-28"
    },
    {
      "budgetItemId": "44444444-4444-4444-4444-444444444444",
      "mode": "Carry"
    }
  ]
}
```

id, effectiveFrom and entries are required. entries may be empty, but cannot be
null, contain null entries, or repeat an item. Notes and the template ID are
optional. Supplied identities cannot be empty UUIDs. Each entry requires
budgetItemId and mode; optional source names snapshotId and entryId, both required
when the source object is present. A selected template can supply the matching
item's source during initial capture. A plan cannot source itself.

| mode | Required value intent |
| --- | --- |
| Confirm | plannedAmount and confirmedOn must both be nonnull. Zero is explicit and valid. |
| Carry | No entered amount/date; copy a resolved strictly earlier compatible source. A copied null remains Unrecorded. |
| Unrecorded | No entered amount/date; save an included unknown amount. |

Mode labels are case-insensitive single names. Numbers, numeric strings, undefined
names and comma-combined labels are rejected. ValueOrigin in responses is a
different enum: Unrecorded, Confirmed or CarriedForward. Frequency continues to use
Week, Month or Year. Feature-local strict converters handle all three without
changing other API enum conventions.

Planned amounts are JSON numbers, nonnegative and below one trillion, with at most
six fractional decimals. Numeric strings are rejected and precision is not silently
rounded. These are planned amounts, so receipt minor-unit precision does not apply.
Confirmation dates are explicit user-check dates, independent of effective dates.
No amount is inferred from zero/default CLR values.

refreshFromSetup deliberately selects current setup context. metadata deliberately
supplies complete historical context; they cannot be selected together. Without
either, capture copies selected source metadata or uses setup for new items;
correction preserves existing metadata unless another context is selected.
metadata requires itemCode, itemName, expenseCategoryId, expenseCategoryName,
currencyCode, frequency, intervalCount and explicit nullable merchantId,
merchantName and notes. Merchant identity/name must be paired. References are
scoped; descriptive names are saved historical labels rather than live lookups.
Changing currency/recurrence cannot silently carry a number across meanings.
New currencies require current support; resolved saved historical currencies may
be retained under the existing service policy.

## Correction and retry contracts

Every correction and entry mutation requires expectedRevision for the parent
snapshot. Header correction also requires effectiveFrom and an explicit nullable
notes field. Amount correction
requires plannedAmount, confirmedOn and notes fields to be present even when null:
two null value/date fields clear the value; a nonnull amount/date pair confirms it
and clears its carry link. Omission is not an instruction to clear a field.

Full entry addition/correction wraps a required entry object beside expectedRevision.
Carry in those operations requires an explicit source, rather than implicitly
reusing the original template. Metadata-only correction wraps a required complete
metadata object beside the revision. It never reads the old source value or
reconfirms the amount, even for detached carried entries. It rejects item-meaning
changes in currency/recurrence; use full explicit value correction for those.
Removal takes only expectedRevision.

Unknown JSON members are rejected at every body/nested level, including creation
hashes, household IDs and read-only identity/provenance fields. Missing mandatory
fields, nulls for nonnullable values, malformed JSON, invalid dates/precision/enums
and empty revisions return 400.
Missing and foreign resources return 404. UUID/date business conflicts return 409;
stale snapshot or affected descendant revisions return 409 titled
"Budget snapshot revision conflict". Existing Problem Details and trace IDs are
used, with unexpected database errors sanitised.

Create idempotency uses the established canonical service hash; no client hash is
accepted and no hash is returned. Matching retries return current retained state,
including corrections, detached sources, removed entries and changed effective
dates. They do not replay value changes. The first create returns Location; a
matching retry returns 200 without a new Location. Clients must retain their
original UUID/payload. After an uncertain correction response, reload the snapshot
before resubmitting with a new revision.

Source removal and meaning/date corrections use the service's atomic detachment
rules. Receiving plan revisions advance when links change, but saved descendant
amounts, metadata, confirmation dates and carried origin remain independent.
Source amount corrections alone do not rewrite copies. The API/UI must show
effective-interval impact before applying backdated date corrections; effective
reads and saved header data expose the dates needed for that flow.

## Read contracts

Snapshot responses include ID/household/date/notes, creation/update timestamps,
revision, optional initial template, IsComplete and saved entries ordered by item
code/UUID. Entry DTOs include their own saved code/name/category/provider,
amount/currency/recurrence/notes, ValueOrigin, LastConfirmedDate and optional carry
source. Internal creation hashes and entity navigation graphs are excluded.
No mixed-currency or mixed-recurrence aggregate total is inferred.

Current-outgoing responses include AsOf, nullable plan ID/effective date/exclusive
end/completeness and items shaped as `{ item, entry }`. item is current setup;
entry is saved historical context or null. Display the amount using entry's saved
currency, frequency and category, even if item defaults differ. Get snapshot detail
to obtain its current revision before editing. A missing effective plan has null
plan metadata; an empty effective plan is known and complete. An omitted entry
does not fall back to an older plan. An included null amount is unrecorded, and
zero remains known. Filtering retired setup items changes displayed items only,
not saved plan completeness. Snapshot/item reads remain household-scoped.

History limits parent plans before joining entries, so pages do not split plans.
Follow nextBefore as before with the desired pageSize. It is ordinary mutable
date navigation; date corrections can change subsequent page membership. Effective
and current-outgoing reads retain the service's repeatable database view across
their queries. No per-item service requests are introduced by DTO mapping.

## Verification

At the initial slice checkpoint, all 128 new HTTP cases passed, covering workflow DTOs/Location, 201/200/409 retries,
nullable and unknown nested fields, value modes, strict numbers/enums, source and
metadata validation, UUID/date/revision conflicts, foreign references, bounded
history, source suggestions, as-of/default-date context, historical currencies,
retired items, explicit re-copy/refresh, detached provenance and transactional
rollback after a failed source deletion. Initial backend regression verification
passed all 1,598 tests (969 data/domain, 629 API), with no failures or skips. The data
assembly completed before a daemon restart; the API project was rerun afterward
to recover its interrupted verification result. The API report is saved at
`/tmp/hearthcast-budget-snapshot-api-final/backend-api.trx`.

Tracked/new file whitespace checks pass. The existing NU1900 warning remains for
unavailable NuGet vulnerability metadata; no packages were added.

The review's nonblocking follow-up moves fallback date resolution out of DTOs and
adds two fixed-clock HTTP cases immediately before/after UTC midnight. Both read
endpoints use the correct date/plan, consult the clock once when defaulting, and
honour an explicit alternate date without consulting it. No household-timezone
subsystem is introduced. All 194 budget-filtered HTTP tests pass after this
follow-up, with no failures or skips, including the setup converter/DI checks.
The full backend suite was not rerun for this clock-testability follow-up; the
earlier full result remains the initial slice checkpoint. No schema changes were needed.

No new dependencies, schema changes, migrations or application deployment are
included. The budget snapshot service is registered in DI and its conflict error
is mapped explicitly. UI remains deferred; backend summaries and reporting period
allocation decisions remain the next feature work.
