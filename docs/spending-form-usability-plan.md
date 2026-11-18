# Spending form usability improvements

Status: implemented and reviewed; final verification recorded below. This records the
approved usability follow-up to the user's first successful itemised receipt.

## Scope

Simplify item creation, make purchase-line selection searchable and clear, connect
category selection to catalogue choice, and reduce the receipt Notes field's
initial height. Use existing Angular Material components and backend contracts.
No schema changes, dependencies, recategorisation of saved data or migration of
existing unit identities are needed.

## 1. Simplify the shared item form

Apply the same presentation to the Items page, its edit sidebar and the inline
Create item dialog:

- Item name, item code, default category, and **Sold by / pack size** are the main
  fields. Keep the user's meaningful item code, e.g. `WR_CHK_THIGH_FILLETS`.
- Replace Unit label with Sold by / pack size, e.g. `Pack of 10`, `Kilogram`, `Each`.
  Explain briefly that quantity 1 represents one such pack/unit and unit price is
  for that pack/unit. This stays descriptive text; do not introduce pack-count
  arithmetic or infer prices per individual thigh.
- Remove the editable Unit / pack code field. Generate the required internal key
  once per new item draft, using a 32-character UUID without hyphens, compatible
  with the existing endpoint. Retain it across input changes and failed saves.
  Renaming the sold-by label must not regenerate the key.
- Preserve all existing item codes/unit keys and price associations. Hide internal
  unit keys in ordinary catalogue row descriptions as well as forms; show the
  readable sold-by label. Existing item codes remain immutable.
- Put optional item notes under a More details disclosure, retaining existing notes
  on edit regardless of whether the disclosure is opened.

Example: Chicken Thigh Fillets / WR_CHK_THIGH_FILLETS / Food / Pack of 10.
A receipt quantity of 2 means two packs; the existing quantity-times-price
calculation stays unchanged.

A generated key is not a user edit. Update draft/dirty/reset logic so clean forms
still permit navigation without a prompt. On a successful create or explicit
clear, start a fresh key and clean validation state. Do not reuse a generated key
from another item or change keys on retry.

## 2. Make line types explicit

The earlier requirement for quick category amounts and mixed-detail receipts
still applies. Replace the ambiguous optional catalogue selector with explicit
line choices:

- **Item**: the default for new lines. Label its picker Item and require an actual
  selected catalogue identity before saving. Quantity and unit price are required
  for newly entered item lines, with existing exact calculation/override rules.
- **Category amount**: an explicit alternative for a quick purchase, category
  split, fee or other amount without catalogue detail. Require category and actual
  amount and omit the item picker/quantity controls. Do not require users to invent
  catalogue entries merely to record a broad amount.

Offer Add item and Add category amount actions, with a small explicit change-type
action where needed, rather than another required dropdown on every line. Line
type is UI state, not a new backend field or duplicated receipt schema. Mixed
records remain supported. Existing category-only lines open in
Category amount mode. Preserve valid historical item lines without quantity/price
pairs; opening an old record must not silently invent values or block unrelated
header corrections.

An explicit mode change must not silently discard entered item/quantity/price or
other line data. Confirm destructive changes when needed. Keep user-entered actual
amounts and receipt descriptions/references/promotion/notes unless the user chooses
to clear them. A blank new item line cannot be saved as an accidental category-only
line.

## 3. Searchable, name-first item choice

Use an Angular Material autocomplete with explicit option selection:

- Search item name and item code, case-insensitively, with sold-by text also
  searchable for distinguishing pack variants. Combine search with category scope.
- Show item name as the primary option text. Put item code and sold-by label in a
  visually secondary line. This distinguishes duplicate names without packing
  everything into one long input value.
- After selection, the input displays the item name; show code/sold-by context
  beneath or alongside it. Search text is not a saved item identity. Typing an
  unresolved replacement search must not leave the previous hidden item selected;
  saving an item line requires explicit selection.
- Show a useful no-match state and retain Create item next to the chooser. Creating
  an item adds and selects it on the initiating line and preserves the receipt.
- Retain marked retired entries where needed for existing records, and preserve
  current draft, household, pending-save and keyboard-focus protections.

No custom dropdown framework or new package is needed. Keep visible secondary
identifiers available to keyboard/screen-reader users as well as mouse users.

## 4. Category-first filtering and item-first classification

For an unselected item line, category is an optional search filter with an All
categories/Choose category state. Choosing it narrows the item options. Selecting
an item then sets the receipt line's category from that item's current default and
locks the category control for that selection. A hint explains that the category
comes from the item.

Changing/clearing the selected item unlocks category filtering. Provide a clear
Change item action; do not silently change a selected item's category to work
around a filter. The inline creation dialog inherits the category filter. If the
new item's category is changed inside the dialog, select that item and use its
actual category on return, keeping the filter and selected item consistent.
Category amount lines retain an ordinary editable required category selector.

On saved-record editing, initialise selected item, category, amounts and copied
labels from the saved line. Preserve its saved category even when today's item
category differs; do not run the new-selection defaults just because the editor
loads or lookups refresh. Explain saved context when necessary. Only deliberate
item reselection uses current catalogue defaults. The existing explicit replacement
warning still applies when replacing classification/lines.

Category locking is a UI entry rule, not a backend invariant. The API must retain
its ability to represent historical classification and category-only receipts.

## 5. Compact receipt notes

Render receipt Notes as a one-row textarea whose initial Material control height
matches receipt total and reference. Keep vertical manual resizing and all existing
text/newlines. Do not add automatic growth on load or input, truncate saved notes,
or force neighbouring controls to stretch. Retain the established grid alignment.

## Verification and delivery

Meaningful tests should cover:

- Generated hidden unit keys are valid and stable across edits/retries, preserved
  for existing items, and excluded from user-dirty state. Successful reset and
  inline-dialog reopening have clean validation and keep catalogue notes.
- Name/code/pack search, duplicate-name disambiguation, no-match/create-and-select,
  explicit selection validation, keyboard operation and focus restoration.
- Category-first filtering, item-first defaulting/locking, clearing/reselection,
  and a newly created item whose category differs from the initial filter.
- Explicit category-only and mixed receipts, required item lines, safe mode
  changes, historical classifications/optional historical quantities, and unchanged
  exact arithmetic and receipt reconciliation.
- Preserved purchase drafts and stale-request/household/navigation safeguards.
- One-row receipt notes retain multiline values and support manual expansion.

Run focused tests, the full frontend suite and production build. Visually inspect
normal and narrow layouts if browser access is available; otherwise disclose that
limit and use rendered Material harness checks. Follow the agreed builder -> one
Astra review -> fixes/checks -> commit/push workflow. Rebuild only the web service
after implementation, as already authorised, without writing test records into the
application database.

Implement the shared item-form simplification first, then the linked line
mode/search/category behaviour as one coherent change, then the compact notes
presentation. All three can be delivered as one reviewed frontend usability slice.

## Implementation notes

The shared item fields hide generated unit identities and collapse optional notes.
The creation draft excludes that generated field from dirty-state checks, and
failed saves retain the key. Historical keys remain untouched. The purchase form
owns UI-only item/category line modes and search text; neither is added to API
payloads. Text entered without an option selection cannot count as a selected item.
Change item returns keyboard focus to the search box. Existing inline creation
keeps its focus-return and pending-write guards.

The focused catalogue/capture/history run passes 94 cases before review. Browser
access remains unavailable; verification uses rendered Material harnesses and
will include the full frontend suite and production build.

Astra reviewed the implementation once. The fixes restrict the historical
quantity/price exemption to an untouched saved selection, ask before discarding
unresolved search text on a line-type change, and explicitly explain that unit
price is for one pack/unit. Regression tests cover the two workflow findings.

Final verification after fixes: **364 tests** across 22 frontend files pass under
`TZ=Europe/London`; production build and whitespace checks pass. No backend tests
were rerun because backend contracts, schema and implementation are unchanged.
No application spending records were created by verification.

New purchase drafts default to GBP, including after save, clear and household
switches. The currency remains selectable; opening a saved record uses its saved
currency and precision.
