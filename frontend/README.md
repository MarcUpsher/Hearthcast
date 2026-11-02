# Hearthcast frontend

Angular Material using the Garmin reference app's azure/blue theme and system
typography, with dark mode enabled. Open http://localhost:4201 with Compose running.
Nginx serves the app and proxies `/api` to the backend on the same origin.

For development, run `npm ci` then `npm start` here. The development server uses
4201 and proxies the API at localhost:5190. Stop the web container first if it
occupies that port, or pass another port to `npm start`.

`npm run build` compiles the production app; `npm test` runs frontend tests.

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
shows all saved entries, with separate tracked-item search/currency filters.
Sidebar form errors and setup refresh errors have independent state.
