# Outgoing plans UI

19 November 2026. Adds `/spending/plans` and its shared Spending navigation link,
using the existing household-scoped budget snapshot API. Backend, database,
dependencies and schema are unchanged.

## Capture and saved plans

Choose an effective date, enter plan notes and record the outgoing amounts together.
The first draft includes all active outgoing definitions; enter a nonnegative
amount (zero is known) or explicitly choose Unknown. Uncheck a row to omit it
from this complete replacement plan. An empty replacement requires confirmation.
Plans remain effective until the next saved plan, rather than being monthly
containers. Backdated captures require an interval-impact confirmation.

New plans resolve the latest strictly earlier source for the chosen date. Matching
entries default to Keep previous, preserving amount, saved descriptive context
and original confirmation date. Unknown source amounts stay unknown. New active
definitions need amounts or an explicit Unknown choice. Retired definitions from
the source remain included and visibly labelled until deliberately unchecked.
Source preparation refreshes setup and shared lookups, and rejects a source with
an unrepresented item so another tab's additions cannot silently disappear.

Changing the effective date requires loading the appropriate source before save.
Replacing entered draft rows requires confirmation; retained notes and a chosen
date remain dirty. Source interval previews follow bounded history pages as far
as necessary to find the nearest later plan, including beyond the visible first
history page. There is no silently truncated total or guessed interval boundary.

Definition options deliberately refresh one row from current outgoing setup.
Currency or recurrence changes in either direction clear its entered amount and
require fresh entry or Unknown. Compatible description/category/provider changes
can preserve the selected value intent. Refreshing setup is distinct from keeping
the historical source context.

The screen shows the effective plan for an explicit local as-of date, complete
saved detail, and descending history with Load older plans. Saved labels, currency,
recurrence, confirmation dates and carried/confirmed/unrecorded states are shown.
Totals group by currency, frequency and interval, using exact decimal arithmetic.
Unknown amounts produce visibly partial known subtotals. No monthly conversion,
mixed-currency total, cash-flow allocation or actual-spending comparison is inferred.

Saved amount corrections use the selected parent revision and narrow amount API,
preserving historical metadata. Clearing the amount explicitly sends null for
both amount and confirmation date; zero remains a confirmed value. Notes belong
to the saved entry. Corrections do not rewrite later plans. Use a new dated plan
for a price change; corrections are for fixing the saved plan. Header/date and
arbitrary saved-entry metadata/list corrections remain separate UI work.

## Precision, state and recovery

Reads retain monetary numeric tokens as decimal text. Writes validate the backend's
full range (below one trillion, at most six decimals), canonicalize using scaled
integers, and encode plannedAmount as an exact JSON number. Ordinary strings use
JSON escaping, and raw response text prevents loss of saved precision. No money
passes through JavaScript Number. The common Api helper supplies an explicit JSON
content type for prepared request text.

An uncertain create retains the original UUID and immutable payload for identical
retry, locking draft inputs. Definitive validation/conflict responses preserve an
editable draft. An uncertain or conflicting correction retains the entered values
and requires an explicit reload to obtain saved state/revision before another
write. That requirement survives cancelling the correction. Failed saves do not
discard entered values. Loading errors block writes and offer retry.

Route, household and unload guards protect edits and block navigation during a
write. Household generation and request version checks ignore late reads and
mutation completions, including leaving and returning to the same household.
Source reloads and selecting another saved plan respect unsaved edits. Correction
inputs receive focus when opened. Definition options collapse; notes start as a
single resizable row and grid fields align at their top edges.

## Verification and review

Astra reviewed once and identified four valid issues: reverse definition changes
could reinterpret an entered amount; stale setup could omit a source entry;
source reloads could mark entered notes clean; and numeric transport rejected
valid high-precision values. All four are fixed with regression coverage, without
a second review.

After fixes, **512 frontend tests across 27 files pass** under
`TZ=Europe/London`; production build and whitespace checks pass. Coverage includes
rendered capture/correction controls, exact request/response transport, full decimal
bounds, carry/unknown/zero semantics, metadata refresh in both directions, newly
added definitions, interval paging, omission/empty plans, retries/conflicts, late
household reads/writes, and shell/route/unload guards. Test writes use mocked HTTP,
not the application database. Backend tests were not rerun because backend code,
contracts and schema are unchanged. Browser visual inspection was unavailable;
rendered Material/component tests cover the UI workflows.
