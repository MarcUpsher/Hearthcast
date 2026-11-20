# Separate purchase list, creation and details pages

20 November 2026. Purchases & history previously combined capture, history and a
selected receipt on one screen. The user approved full-page creation and details,
with read-only details by default and explicit Edit, Save changes and Cancel.
The existing backend and schema remain unchanged.

## Navigation and page responsibilities

| Route | Behaviour |
| --- | --- |
| `/spending/records` | Purchases & history: date/void filters, bounded paging, summary rows, Record purchase and View details links. Loads history only. |
| `/spending/records/new` | Full-page creation using the existing capture component, including inline item creation, additional costs, discounts and exact retries. |
| `/spending/records/:recordId` | Full-page saved receipt, fetched using the selected household. Starts read-only. Edit fetches a fresh revision and opens the existing capture form on the same page. |

The literal `new` route precedes the record-ID route. Direct record links and
refreshes fetch authoritative details rather than relying on a previously selected
list row. Missing/foreign records show an error with Retry and Back to purchases;
they never open an empty creation form. Saved merchant/item/category labels,
currency precision, reconciliation, costs, discounts and void/refund presentation
are retained. Refunds and voided records retain the existing read-only policy.

Editing fetches a fresh record before applying changes. Save returns to the updated
read-only view; Cancel uses the capture component's existing discard confirmation
and returns to the latest saved version, including after an editor reload. Adding
catalogue items continues inside the capture form. Entry choices are loaded only
for creation or editing, so viewing history or a receipt does not load catalogue
inventories. Failed choice reads retain the editor with explicit Retry controls.

Successful creation opens its saved record URL with `replaceUrl`, so browser Back
returns to the list rather than an abandoned blank creation form. The success
notice is passed once through household-scoped navigation state. An uncertain
create retains its original draft, UUID and request until the existing recovery
or explicit abandonment flow resolves it.

## Returning to the list

`SpendingHistoryNavigation` retains the current household's last successful page,
applied and unapplied filter values, current cursor, previous-cursor stack and
scroll position in memory. Browser Back and Back to purchases restore them after
rendering the list. This is session navigation state, not saved receipt data or
cross-reload persistence. Switching households clears it, including A → B → A.

Create/edit/void operations mark the retained page for refresh. Returning refreshes
the same cursor under its original applied filters without resetting to page one
or silently applying unfinished filter changes. A failed refresh retains the old
page with an explicit changed-data notice and retryable error. Existing keyset
paging semantics still apply: edits can change which records appear at that anchor;
the page is not a frozen historical snapshot. Manual Apply filters / refresh starts
at the first page for the newly applied filters.

## Drafts and asynchronous work

Create/details routes use `canDeactivate`; household switching uses the same page
guard through the app shell. Active writes block leaving, unsaved changes require
confirmation, and browser unload warns. This includes inline item drafts, pending
item creation and uncertain creates. Changing record parameters on the same route
also runs the guard. Explicit Edit/Save/Cancel avoids an ambiguous edit toggle.

Household, record, request and component-lifetime generations reject stale reads
and completions, including after leaving a page or switching households away and
back. The list has no writes or draft guard. Voiding remains an explicit,
revision-checked action with confirmation and mandatory reload after conflicts.

## Verification

Existing item-dialog integration tests moved to the purchase page. List tests cover
bounded requests, filter validation, paging, failed refreshes, retained navigation
state and household races. Detail tests cover read-only loading, costs/discounts,
fresh edit revisions, save/cancel/reload, invalid records, stale responses and voids.
Router integration tests exercise actual routes, browser Back, saved-creation URL
replacement, parameter-change guards, retained list scroll and uncertain writes.
The shell tests now exercise creation and saved-record editing on their own routes.
Astra reviewed once and identified two correctness issues. Sol fixed both without
a second review pass:

- Ambiguous create/edit/void failures now mark the retained list for refresh, since
  the server may have committed despite a lost response. The uncertain draft and
  cursor remain intact. Route tests exercise returning after each failure type.
- Capture validates household and expected record ID before accepting save,
  reload or uncertain-create recovery responses. Mismatches retain the draft and
  original target; they never substitute another record or clear the submitted ID.
  Regression tests cover mismatched IDs/households and subsequent writes.

After the fixes, all **641 frontend tests** across 33 files pass under
`TZ=Europe/London`. The production build and whitespace checks are verified before
rollout. Backend tests are not rerun because backend code, contracts and schema
are unchanged. Deployment checks are recorded below.

The web container is deployed and healthy; API/database services were not
restarted. Read-only live checks confirmed the exact tested main, list and
purchase bundles, a list bundle without the embedded capture form, the creation
route, five direct saved-record routes, and scoped receipt/report reads. No
application records were written. Browser access reported an unavailable host,
so visual browser inspection could not run; rendered Material and router tests
cover the interaction and navigation paths. No dependencies were added.
