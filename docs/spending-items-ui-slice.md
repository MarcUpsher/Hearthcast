# Spending item catalogue and usual prices UI

18 November 2026. Adds `/spending/items` as the Spending landing page, with shared
navigation to Categories & merchants. Uses existing backend contracts only; no
schema, dependency or backend changes.

## Catalogue

Users create catalogue items with unique code, name, default expense category,
unit/pack code, unit label and optional notes. Code and unit identity are fixed
after creation. The right edit sidebar changes only name, category, label and
notes; blank notes become explicit null. A different unit/pack should use another
item identity. Category options include clearly marked retired categories so
existing metadata remains editable. Items are filtered by their own retirement
flag, not their category's flag.

Search covers item names/codes, units, notes and category names. Category and
Active/Retired/All filters combine with search. Retire/Restore are separate actions;
no delete action is offered. Failed saves retain the complete draft. Successful
creation resets the whole form's value and submitted/touched/dirty state.

## Usual prices

Select Usual prices on an item to maintain explicit defaults per merchant and
currency. The panel reads all saved prices for that item. Merchant choices include
marked retired entries, matching historical-use API semantics. Currency is an
explicit three-letter code; setup prices are not restricted or rounded to the
receipt currency policy. Values allow zero and up to six decimal places, below
one billion. Grouped input spaces are removed without rounding; display preserves
all six supported decimals and includes the currency code.

Creation sends item, merchant, currency and the chosen numeric price. Editing
locks that identity and sends only `expectedRevision` and `usualUnitPrice`.
Reconfirming an unchanged value is valid. Existing merchant/currency combinations
must be edited rather than silently overwritten. A duplicate-create race returns
an error and preserves the entered values for explicit recovery.

On a stale update, the draft is retained and saving is blocked until the user
chooses Reload saved price. That read displays the current saved value and adopts
its revision while preserving the proposed amount. It never automatically retries
a write. A cancelled edit cannot be revived by a late revision read. Price rows
display their saved unit labels and update timestamp; changing an item's current
label does not reinterpret those copied labels.

These prices are future-entry defaults, not recorded purchase amounts. Price
maintenance never creates spending or implicitly adopts an actual purchase price.

## State and navigation

Reads and writes capture household/item identities and generations. Older retry,
previous-item, previous-household and destroyed-component results cannot overwrite
current state. The catalogue owns the item draft and editor; the price panel owns
its own draft, loaded revisions and conflict state. Parent guards include nested
price drafts and in-flight writes. Changing the selected price item or closing its
panel asks before discarding changes; writes block navigation and household/item
switches. Browser reload receives the usual unsaved warning. Sidebar history
navigation remains owned by the route guard (`closeOnNavigation: false`).

The shared Spending navigation uses Items and Categories & merchants. The main
Spending link stays active for either route. Merely opening either screen reads
data; no defaults or entries are seeded.

## Verification

Catalogue and price component tests cover full request contracts, successful form
reset, failed saves, immutable identities, retirement, filters, empty/retry states,
zero/six-decimal prices, revision conflicts, explicit recovery and stale requests.
Shell/router tests include catalogue drafts, pending writes and browser Back.
Before review the full frontend suite passed 213 tests, followed by a passing
20-case catalogue run adding household-switch form-reset coverage. Astra's single
review identified that reloading the price list could leave an open editor using
an older revision while calling its value current. Reload now detects that
revision change, preserves the typed amount, labels the old value as previously
loaded and blocks saving until the explicit saved-price reload. A regression case
verifies this flow. After the fix, the complete frontend suite passes **215 tests**
across 14 files, and the production build passes. No automatic second review.

Browser access was checked again and remains unavailable, so a live visual check
could not be completed. Component tests exercise the rendered Material forms and
sidebar lifecycle. No production records were created by tests.

The subsequent [form usability update](spending-form-usability-plan.md) hides and
generates unit identities, labels the visible field Sold by / pack size, and
collapses optional notes. The original field list above records the first slice.
