# Spending categories and merchants UI

18 November 2026. Adds a Spending navigation entry and a lazy-loaded
`/spending/setup` page for the selected household. It uses the existing
[spending setup API](spending-setup-api-slice.md), Angular Material theme and
shared transport/error handling. No backend, schema or dependency changes.

## Behaviour

Categories and merchants have independent lists and inline name-creation forms.
Both lists share search and Active/Retired/All filtering; Active is the initial
selection. A failed list does not prevent the other from loading, and its Retry
reloads only that list. Loading, genuinely empty and filtered-empty states are
distinct. Names are user-defined; no setup entries are seeded.
After a successful add, that creation form resets its submitted/touched/dirty
state along with its name. The cleared required field is ready for another entry,
without a red validation highlight. The other list's draft and failed-save drafts
are preserved. Both creation regression cases verify the rendered error state.

Edit opens a right-hand Material dialog with the name input focused. Save sends
only the name to the resource's `/name` endpoint. Cancel discards that edit without
affecting creation drafts. Escape or backdrop dismissal asks before discarding a
changed name. Browser-history changes leave the dialog open until the route guard
allows navigation; rejecting Back preserves the sidebar and typed name, including
while a save is pending. Rejected saves retain the draft and show the API error for retry.
Retire and Restore are separate actions sending an explicit `isRetired` value.
Saved spending history retains its copied category and merchant metadata.

Creation sends POST to the household's spending categories/merchants endpoint;
renaming and retirement use PUT. Lists request `includeRetired=true` so retired
entries remain discoverable through the filter. Responses update the local list
directly without a second read that could disguise a successful write as a failed
save. Search/retirement filters remain in effect after saving.

The models live under `frontend/src/app/spending/`, separate from the asset/debt
classification setup. Opening the page only reads data. Users without a selected
household see guidance to Household setup.

## Household and draft protection

Each request captures its household and page generation. Late results/errors from
another household, a prior visit to the same household or a destroyed page are
ignored. Per-list request versions also prevent an earlier retry from overwriting
a newer result. Switching households clears the old lists and drafts before new
data is shown; no household identifiers are hard-coded.

The page's `canLeave` participates in both the route guard and the app shell's
household-change guard. Creation and changed-name drafts require confirmation
before leaving. A write in progress blocks navigation, household switching,
duplicate submissions and dialog cancellation. Browser reload/close also receives
the native unsaved-changes warning. Forced external household changes still cannot
let an old completion clear the new household's busy/draft state.

## Verification

The complete frontend suite passes: **167 tests** across 12 files. This includes
23 spending component cases and eight added shell/router integration cases covering
setup creation/renaming/retirement, name-only and retirement payloads, failed saves,
partial load/retry, status/search, household races, unsaved edits, pending writes,
browser-unload protection and dialog focus/dismissal. Dialog tests wait for the
actual focus/close lifecycle rather than assuming animations finish synchronously.
`npm run build` also passes; the new page is lazy-loaded.

Astra low's single review found that Material's default history-navigation close
behaviour could discard a rename before the route guard ran, or close a pending
save's editor while leaving the route active. Sol reproduced both failures with
history-navigation tests, set `closeOnNavigation: false`, and reran the full checks.
No second review pass was requested.

The browser access host was unavailable during implementation, so a live visual
check of desktop/mobile layout and keyboard behaviour could not be completed.
Rendered component tests verify controls and dialog interaction; responsive styles
provide two columns on wide layouts and one below 950px, with wrapping controls
for narrow screens. This limitation is separate from the passing automated checks.

No app deployment or application-database migration is part of this slice. The
next proposed slice is quick spending capture and bounded history, to be discussed
after this slice is reviewed, fixed, committed and pushed.
