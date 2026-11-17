# Budget snapshot services

17 November 2026. BudgetSnapshotService implements complete dated-plan capture,
carry-forward, corrections and reads over the [budget domain](budget-domain-slice.md).
[Setup services](budget-setup-service-slice.md) continue to own reusable defaults.
[Budget setup HTTP endpoints](budget-setup-api-slice.md) are implemented. Snapshot
HTTP contracts/endpoints and UI remain subsequent work.

## Inputs and capture

CreateAsync takes a household, client-generated snapshot UUID and
BudgetSnapshotValues: EffectiveFrom, selected Entries, optional
CopiedFromBudgetSnapshotId and notes. An empty entry list is an explicitly empty
complete plan. Included unrecorded entries are incomplete; omitted items are outside
the plan. New setup items are never silently added. Retired setup identities remain
explicitly usable. There is no physical snapshot deletion operation.

Each BudgetEntryValues names a BudgetItem and selects a BudgetEntryValueMode:

| Mode | Input and resulting value |
| --- | --- |
| Unrecorded | No amount/date. Saves null amount and confirmation, with no carry link. |
| Confirm | Explicit nonnegative amount (including zero) and confirmation date. Saves Confirmed and clears carry provenance links. |
| Carry | No entered amount/date. Requires a source entry and copies its independent saved value and confirmation. A known value becomes CarriedForward; a copied null remains Unrecorded. |

An optional BudgetEntrySource names a source snapshot/entry. Otherwise, during
creation, the selected template supplies the same item's source entry when present.
The template is recorded independently of individual value choices or omissions.
Every supplied source must belong to this household, contain the selected item
and be strictly earlier than the receiving plan. An explicit source may differ
from the initial template.

Metadata defaults to the selected source's saved context. Without a source,
new entries use current setup. During correction, existing metadata is retained
unless a new source, RefreshFromSetup or Metadata override is explicitly selected.
RefreshFromSetup copies current category/provider/name/currency/recurrence defaults;
Metadata is a complete manual historical context with scoped identity references.
These two choices are mutually exclusive. The value mode remains explicit: a
refresh cannot silently carry a number into a different currency or recurrence.
Item, currency, frequency and interval must match when carrying.

Saved historical currency codes can be preserved from a resolved source even when
explicitly confirming a new amount, or retained during corrections. This uses
internal resolved saved context; callers cannot set an allow-unsupported flag.
New unsupported currency selections remain invalid. Planned amounts retain the
six-decimal bounds and no currency conversion or recurrence conversion is inferred.

Capture batches distinct budget items, explicit source snapshots and category/merchant
references within each transaction attempt. A template already loaded by the request
is reused; each other distinct source graph is loaded once. Repeated explicit
references do not issue full-source queries per entry. These resolved references
are private to that attempt and are rebuilt after a serialization retry. Retained
create replay still returns before reference resolution.

## Create retries and conflicts

BudgetCreateRequest freezes the caller's entry collection before the first await.
Version-one SHA-256 hashing covers only canonical request data: household/snapshot
IDs, effective date, template, notes and entries sorted by item UUID, including
value mode, explicit amounts/dates, source IDs and metadata/refresh intent. Text
and codes use shared normalisation; dates and G29 decimal strings use invariant
culture. Entry order has no stored meaning. Current setup/source metadata and
revisions do not enter the hash. A fixed independent hash fixture protects the
persisted format.

An identical UUID/hash returns WasCreated false and the retained plan's current
state, before looking up current sources or reserving its original date. It does
not recarry, reconfirm, add entries back or repeat corrections. This remains true
after source removal, source-link detachment, date correction or emptying a plan.
Changed request data or household reuse conflicts. New creates return WasCreated
true. A household has one plan per effective date; distinct UUIDs competing for
the same date produce one business conflict.

Writes use SerializableTransaction. A concurrent identical UUID can lose either
the primary-key or effective-date index race; the service recognises the retained
create in either case. Date collisions and UUID collisions are business conflicts;
unrelated database errors propagate. Confirmed serialization/deadlock aborts may
retry in a fresh context; ambiguous network/commit failures are not replayed.

## Corrections and source links

All mutations require the saved parent revision. Empty revisions are validation
errors, stale ones raise BudgetSnapshotConflictException, and child changes advance
the parent's revision. Operations include header/date correction, amount correction,
metadata-only correction, explicit full entry correction, addition and removal.
New entries receive new UUIDs; correcting an entry retains its UUID. Snapshot
UUID, creation time and immutable create hash are retained throughout.

CorrectEntryMetadataAsync never consults a carry source. It preserves the saved
amount, confirmation date, ValueOrigin and link for linked/detached carried,
confirmed and unrecorded entries. Item identity/currency/recurrence meaning changes
require the explicit full value operation. CorrectAmountAsync confirms a supplied
nonnull value, including an unchanged one; null clears it to Unrecorded. Neither
source amount nor metadata-only corrections rewrite independent descendants.

Full entry corrections compare direct incoming carry links against the corrected
source item/currency/recurrence. Only incompatible links are cleared. Descendant
metadata, amounts, confirmation dates and origin remain unchanged; their parent
revisions advance. Compatible links and downstream links to unchanged descendants
remain intact. Template links remain informational and independent of entry removal.

Removing a source entry first detaches incoming carry links and saves those
revision-protected changes before deleting the source row, all inside the same
outer transaction. A failure after that intermediate save rolls back every link,
revision and source change. No composite foreign key nulls HouseholdId.

Date corrections detach incoming/outgoing template and carry links that would
cease to point strictly backwards. Compatible links stay. The normal effective
interval changes, but saved descendants are not rebuilt. The API/UI must show the
old/new interval impact before accepting backdated changes.

Affected descendant revisions observed on the first attempt remain expectations
across serialization retries. A concurrent descendant edit conflicts even if it
removed the dependency link before retry. This prevents a retry from quietly
reinterpreting the affected set after user changes. Every affected parent update
and the source correction commit or roll back together.

## Reads

| Operation | Behaviour |
| --- | --- |
| GetAsync | Complete household-scoped retained snapshot, with saved entries. |
| ListAsync | Bounded descending effective-date history, default 25/max 100. NextBefore is an exclusive date continuation. Parent limit/lookahead precedes entry joins, so plans are never split. Continuation is not a frozen multi-page view. |
| FindCopySourceAsync | Latest plan strictly before a proposed effective date, as a suggestion only. Create still supplies its source choice explicitly. |
| GetEffectiveAsync | Latest plan on/before a supplied date, plus the next plan's effective date as EffectiveUntilExclusive. Null before the first plan. |
| GetCurrentOutgoingsAsync | Current setup items alongside their saved entry from the effective plan. Includes plan ID/date/end/completeness, with null plan metadata if unknown. Missing entries never fall back to an older plan. |

As-of dates are explicit service inputs; the future API/UI supplies the appropriate
"today" default. The effective-plan and current-outgoing multi-query reads use
RepeatableRead so the plan, interval and setup inventory describe one database
view even if another plan is inserted between queries. Current outgoing reads
are batched, with no per-item plan queries. Setup currency/recurrence/name may
differ from saved entry context and are returned separately. No mixed-currency or
mixed-recurrence aggregate total is exposed.

Current-outgoing lists include retired setup by default. Filtering it hides only
those displayed items, not entries from the saved plan's completeness evaluation.
An effective empty plan is known and complete; no effective plan is unknown.
An included null amount is unrecorded; an absent entry is outside that plan.

## Verification and boundary

Workflow and request tests cover null/zero/omission, saved metadata versus explicit
refresh, historical/retired capture, hashes and caller collection freezing, create
races/retries, dates, UUID/household isolation, corrections, direct source-detachment,
transaction rollback, affected revision conflicts, bounded history and consistent
as-of reads under concurrent writes. At the initial slice checkpoint, all 63 new
cases passed (32 request/canonical format cases and 31 service workflows). The
clean full backend regression run passed all 1,404 tests (966 data/domain, 438 API), with no failures or skips,
including migration/model consistency checks. Whitespace checks pass for tracked
and new files. The existing NU1900 warning remains for unavailable NuGet
vulnerability metadata; no packages were added.

The full run exposed an existing household concurrency test's exact-two-save
assumption when a valid serialization retry produced a third save arrival. Its
overlap assertion now permits retries, matching the existing tracked-item and
financed-asset tests; one success, one business conflict and the final single
participant invariant remain required. No household runtime code was changed.

Review follow-up adds three cases: two verify bounded capture query counts for
18 entries using either shared-template or explicit source references, and one
forces a serialization retry after a source-value change to verify fresh lookups.
All 193 budget-filtered tests pass after the follow-up, with no failures or skips.
The full suite was not rerun for this query-only follow-up; the earlier full result
above remains the initial slice checkpoint.

No additional tables, dependencies, migration or HTTP registration are needed.
The effective-date index's existing name is explicitly shared with conflict
handling. No application database migration/deployment is performed by tests.
The subsequent [setup API slice](budget-setup-api-slice.md) exposes reusable defaults.
Next: budget snapshot API contracts/endpoints; reporting allocation and UI remain later work.
