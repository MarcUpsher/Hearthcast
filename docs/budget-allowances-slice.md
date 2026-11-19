# Budget commitments and category allowances

19 November 2026. Implements the approved [slice plan](budget-allowances-slice-plan.md)
using the existing setup items, dated snapshots and entry APIs. No parallel target
aggregate, monthly snapshot lifecycle or automatic spending comparison is added.

## Domain and history

BudgetItemKind is Commitment (1) or Allowance (2), immutable on a setup identity
and copied into every saved entry. Explicit metadata must agree with the item's
kind. Existing definitions and historical entries migrate to Commitment, without
guessing from names or categories. The migration adds defaulted Kind columns,
constraints and filtered unique indexes while preserving existing category indexes.
Existing amounts, confirmation dates, revisions, timestamps, hashes and source
links remain unchanged. Wrong-kind definitions can be retired and replaced.

Allowances have a spending category, currency, recurrence and optional notes.
Their codes are generated from random UUIDs; the initial internal name comes from
the category. Merchants and payment dates are rejected. Setup creation accepts
the allowance variant through POST budgets/items; omitted kind retains legacy
Commitment creation requiring code/name. All reads expose named kind strings.
Details updates retain kind, and unknown kind update fields are rejected.

One active allowance per household/category/currency is enforced even across
recurrences. Plans permit at most one allowance per saved category/currency,
including retired/replacement identities. Domain validation, database indexes and
specific conflict mapping protect both invariants. Multiple commitments in one
category remain valid. Retired definitions keep their existing saved entries.

Carry compatibility and incoming-link detachment include kind and, for an
allowance, category identity. Moving an allowance from Food to Pharmacy requires
explicit confirmation or Unknown. Metadata-only correction cannot reinterpret
the amount. Existing commitment category semantics are retained. Candidate
validation occurs before saved entries mutate; transactions roll back conflicts.

Legacy version-one create hashes remain byte-for-byte compatible, including
explicit default-kind metadata. Explicit Allowance metadata selects version two
and includes kind; hash version selection depends only on frozen request data.
No stored hashes are recalculated. Matching creates still return retained current
state after later corrections, without replaying mutations.

## UI

Existing overview/plan URLs remain, with navigation labelled Budget overview and
Budget plans. One dated plan has Regular commitments and Spending allowances
sections, one effective date/revision and one Save action. Editing an allowance
does not omit unchanged commitments. The commitment setup screen excludes
allowance definitions; there is no extra navigation tab.

Inside Budget plans, allowance management selects a category and defaults to
GBP/every month. Currency, period and notes are optional details. Codes, separate
names, merchants and dates are absent. Adding a reusable definition selects its
plan row; the amount is entered there. An existing active category/currency
allowance is reused rather than duplicated. Editing/retiring/restoring defaults
does not alter saved amounts. A setup definition is saved immediately and can
remain unused if the user later discards their plan; no atomic setup-and-plan
save is implied.

Entered allowance amounts confirm on the explicit local date. Unchanged carries
preserve their confirmation date. Confirmation/unknown controls remain available
under Amount details. New definitions require an amount or explicit Unknown.
Internal allowance codes/names are hidden in saved/detail/overview rows, which
use the saved category label. Payment schedules are shown only for commitments.

Allowances are additional to listed commitments. GBP 400 Food allowance plus a
GBP 50 Food subscription earmarks GBP 450, not GBP 400. The editor explains this
and exposes category subtotals. Purpose subtotals and combined earmarked totals
keep currency, frequency and interval separate, using exact decimal arithmetic.
Unknown values make relevant totals visibly partial. No conversions, proration,
implicit category cap or scheduled cash total is inferred.

The parent draft guard includes allowance setup edits and active writes. Existing
route/household/unload protection and generation/version guards remain in force.
Uncertain/conflicting definition creation requires a scoped reload and explicit
use of an existing matching allowance, or a deliberate retry after none is found.
The rest of the plan draft is retained. Full plan retries preserve their frozen
UUID/payload. Saved corrections remain revision checked.

## Verification

Full backend regression passed 1,010 data/domain and 673 API cases. After adding
two further database/replay cases, all 13 allowance data tests also pass; these
extend verification to the current 1,012 data/domain cases. The independent v1
golden hash, migration round trip, unchanged legacy retry, v2 explicit metadata
retry after correction, active-definition races, filtered-index backstops,
source detachment and mixed/read HTTP contracts are covered. Only the existing
NU1900 unavailable vulnerability-feed warning occurred; dependencies are unchanged.

Frontend component/shell tests cover mixed-plan capture, additive totals, lean
rendered category/amount controls, no allowance schedules/codes, generated/reused
definitions, defaults/retirement, validation/reset, uncertain create reconciliation,
saved/source meaning changes, duplicate rows, context races and parent guards.
All writes in verification use disposable test databases or mocked HTTP, never
the live household. No live browser visual inspection was performed.

Astra reviewed once and identified three UI issues: refreshed source context
could become stale after a defaults edit; saving amounts could discard unfinished
setup; and Keep previous could display a different draft number. All are fixed
with regression cases. Current-default rows rebuild their context and clear an
amount if its meaning changes, setup drafts/reconciliation must resolve before
plan or correction saves, and Carry displays its actual source value (including
unknown). No second review was requested.

After fixes, **566 frontend tests across 29 files pass** under `TZ=Europe/London`;
production build and whitespace checks pass. Backend product code is unchanged
since its full green regression run. Migration deployment is preceded by a private
database backup and a read-only hash manifest of existing budget rows; rollout
checks compare their non-Kind columns and verify the new schema/API/bundles.
