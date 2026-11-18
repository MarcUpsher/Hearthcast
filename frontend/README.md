# Hearthcast frontend

Angular Material using the Garmin reference app's azure/blue theme and system
typography, with dark mode enabled. Open http://localhost:4201 with Compose running.
Nginx serves the app and proxies `/api` to the backend on the same origin.

For development, run `npm ci` then `npm start` here. The development server uses
4201 and proxies the API at localhost:5190. Stop the web container first if it
occupies that port, or pass another port to `npm start`.

`npm run build` compiles the production app; `npm test` runs frontend tests.

- History: date-range and interval controls, currency views, household/category and
  person investment lines with hover readings.
- Spending: household-scoped category and merchant setup, with inline creation,
  name-edit sidebars, retirement/restoration, search and status filters. Unsaved
  drafts are protected on navigation and household switching; failed saves retain
  entered names. Category spending reports remain subsequent UI work.
  See the [spending setup UI slice](../docs/spending-setup-ui-slice.md).
  The Spending landing page is now Items: catalogue creation/editing, immutable
  code/unit identity, category/search/status filters, retirement/restoration and
  per-item merchant/currency usual-price maintenance. Price conflicts preserve
  the draft and require an explicit reload before applying an amount with a new
  revision. Zero and six-decimal usual prices are supported. See
  [items and prices UI](../docs/spending-items-ui-slice.md).
  Purchases & history supports quick category amounts/splits and catalogue lines
  with quantities, overridable usual prices and actual line amounts. Save retries
  retain the submitted client identity; uncertain outcomes can be checked before
  another write. History has inclusive date filters, bounded pagination, saved
  details and revision-checked voiding. Saved purchases can be edited with narrow
  corrections preserving labels and deliberate wider replacements; conflicts retain
  local edits until an explicit reload. See [purchase editing](../docs/spending-edit-ui-slice.md) and [capture/history UI](../docs/spending-capture-ui-slice.md).
- Modelling: snapshot-based native-currency scenarios, multiple loan payoffs,
  reusable/default presets, real/nominal display, growth breakdown and expense targets.
  See the [current modelling guide](../docs/modelling.md).
- Snapshots: available year/month/date selectors; household contribution,
  individual ownership and all-recorded views; category/group totals and
  searchable recorded entries with carry/confirmation information.
- Record snapshot: fresh capture, next capture with a carry source, or revision-
  checked correction. Unknown and zero are distinct. Corrections send only changed
  rows. Entry supports nonnegative values with up to 12 whole digits and two decimal
  places. Unedited readings retain their backend precision.
  Entry is grouped by actual holder, category, group (Pension, TFSA, etc.) and alphabetical item name. Joint
  assets appear once under their combined owners, with balance fields kept together.
  Corrections use the holder/category/ownership copied into the saved snapshot.
- Setup: households/people, categories/groups, item/financed-asset creation,
  ownership/household allocation, borrower selection and retirement/activation.
  Fields unsupported by existing update endpoints are not editable.
  Households, people, categories, groups, tracked items and financed assets use
  an Edit action opening a Material sidebar on the right. Each item has one
  Edit action for its name, metadata and household allocation; financed assets
  combine name, owners, borrowers and household allocation. These changes save
  atomically. Cancel discards unsaved changes; rejected edits remain open with
  the error and entered values. Editing preserves identity and saved snapshot
  details. Editing and activation are independent actions. Creation remains
  inline. Bulk snapshot editing retains its full-page form, entered through
  the snapshot's Edit link.
  Tracked items also have optional Institution / platform and Fund name fields,
  available during creation and through Edit. Clearing a field
  removes its current value. Setup search includes both fields. New snapshots
  copy the metadata and display it alongside the item; existing snapshots and
  corrections retain their saved metadata. Historical entries remain blank
  unless metadata was recorded when they were captured.

Setup includes a “Present in latest snapshot” filter, independent of checklist
activation and text search, plus last-confirmed dates. Carried entries remain in
the latest filter without receiving a newer confirmation date. Financed assets
show confirmation dates for each balance separately. A read-only
`GET /api/households/{id}/snapshots/activity` supplies this metadata in one request.
Tracked-item and financed-asset column headers toggle ascending/descending sorting.
Their sort choices are independent and combine with the existing filters. Dates
sort chronologically, loan amounts numerically, and unknown dates stay last.
An account-holder filter selects tracked items by actual holder. For financed
assets it selects owners, including jointly owned assets for either owner;
household allocations and borrower links do not determine this filter.

Households are discovered through GET `/api/households`. No private IDs or data
are embedded in the frontend; the selected household ID is remembered locally.
Opening pages never changes financial records. Capture uses the active registry;
select current items before recording a future snapshot.

Currencies remain separate and calculations stay in the API. Summary completeness
describes included entries, not proof of comprehensive historical coverage.
Setup changes do not rewrite saved snapshots. Charts, FX, projections and import
screens are outside this slice.

Snapshot entry disables saving until the selected source is loaded successfully.
Leaving historical correction restores the capture date/precision. Navigation,
mode changes and source changes prompt before discarding unsaved snapshot work;
closing or reloading the browser uses its native unsaved-changes warning.
The summary selectors apply to the totals; the section labelled Entire snapshot
shows all saved entries, with separate search/currency filters.
Sidebar form errors and setup refresh errors have independent state.

Tracked items open in person/group order, then newest confirmed reading first
(unknown dates last), then active before retired, with name as a tie-breaker.
Column headers override this order; Restore default order returns to it.

The checklist filter defaults to Active on opening setup. Select All or Retired
to find older tracked items or financed assets.

Displayed balances use the currency symbol, spaces between thousands and two
decimal places (for example R 12 028.00 or £ 12 028.00). Monetary inputs show the
symbol separately and group digits on blur; focusing reveals the raw editable
value. Pasted spaces are accepted. Formatting never rounds the input model or
changes existing snapshot values; API payloads remain unformatted.

When editing a saved snapshot, Add item lists only currently active tracked items
absent from it. Selected additions appear in their holder/category/group section
with a New addition badge and their household allocation. Enter a value or choose
Unknown; Cancel addition removes only a pending addition. Save corrections submits
additions and corrections atomically with the saved revision. Existing retired
entries remain present. The chooser does not add financed assets.

Recorded entries on the snapshot view use saved holder/owner, category and group
headings, with items alphabetized and consistent table columns across groups.
Joint financed assets appear once under their combined owners. Search and currency
filters apply to both ordinary items and financed assets.

New tracked items and financed assets use Country and Currency dropdowns. Country
changes default the currency; users can then choose a different denomination.
Countries without a default require an explicit currency choice. Defaults are
checked-in Unicode CLDR 49 reference data (2026-11-03), with English display names
provided by Intl.DisplayNames; there are no runtime lookup requests or new npm
dependencies. The data source is
[Unicode CLDR](https://github.com/unicode-org/cldr-json/tree/main/cldr-json/cldr-core/supplemental).

Summary groups have collapsed disclosure arrows revealing their constituent items
and financed-asset equity. The detail rows come from the selected report scope,
including the selected person's household allocation or ownership share. Values
are API-provided contributions; debts display as deductions, and missing values
remain Unknown. Metadata and carried/confirmed status are shown alongside names.

The snapshot viewer starts with a combined summary, showing each category/group
and expandable holding in every currency represented in the saved snapshot.
Conversions and allocations are calculated by the backend using saved rates.
Missing values or rates are marked incomplete. The original native-currency blocks
and full recorded entries remain below it. See `../docs/merged-summary-slice.md`.

Frontend contracts live with each feature; transport/errors and Workspace have
separate core files. Shared chart interpolation lives in `shared/charts`. Modelling's
parent coordinates drafts/requests; chart and result components only present reports.
See the [project map](../docs/project-layout.md) and [docs index](../docs/index.md).
