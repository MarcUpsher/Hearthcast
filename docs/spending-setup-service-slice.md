# Spending setup — service layer

17 November 2026. Builds on the [domain/persistence slice](spending-setup-slice.md)
and the [spending/budget plan](spending-and-budget-schema-plan.md).

## Implemented boundary

`SpendingSetupService` takes an IDbContextFactory and provides household-scoped
creation, lookup, inventory and editing for expense categories, merchants,
spending items and explicitly remembered usual prices. The service slice itself
needed no schema change; subsequent review hardening is documented in the
[domain/persistence note](spending-setup-slice.md). The subsequent
[API slice](spending-setup-api-slice.md) now provides controllers, wire contracts,
registration and HTTP exception mapping. There is no UI. Spending records, budget
workflows and receipt-source links remain deferred.

## Operations and semantics

| Area | Operations |
| --- | --- |
| Expense categories | CreateCategoryAsync, GetCategoryAsync, ListCategoriesAsync, RenameCategoryAsync, SetCategoryRetiredAsync |
| Merchants | CreateMerchantAsync, GetMerchantAsync, ListMerchantsAsync, RenameMerchantAsync, SetMerchantRetiredAsync |
| Items | CreateItemAsync, GetItemAsync, ListItemsAsync, UpdateItemAsync, SetItemRetiredAsync |
| Usual prices | CreateUsualPriceAsync, GetUsualPriceAsync, ListUsualPricesAsync, FindUsualPriceAsync, UpdateUsualPriceAsync |

Root creates/inventories require an existing household. Every referenced identity
is loaded with its household predicate. Missing identities and foreign-household
identities both return ResourceNotFoundException, without revealing foreign data.

Category/merchant/item inventories include retired rows by default for maintenance
and historical selection. Passing includeRetired=false filters by that entity's
own retirement flag. Retirement neither cascades nor hides an active item because
its category was retired. Get and explicit-reference operations allow retired
setup, so deliberate historical use and corrections remain possible. Normal
active selection can use the filter; no extra historical-mode flag is needed in
these setup services. All retirement operations are reversible. No deletion
operation is exposed.

Category/merchant lists sort by name then UUID. Item lists sort by name, code and
UUID. Price lists are scoped to one existing item and sort by currency, merchant
UUID and price UUID; they include prices for retired merchants/items. Reads return
entities from a disposed context; changing them does not implicitly persist data.

UpdateItemAsync saves name, category, unit display label and notes as one operation.
Code and UnitKey remain immutable. A changed display label never changes the
price's copied label or remembered amount. Category and merchant names remain
nonunique; only normalized item codes have household uniqueness, including retired
items. Duplicate codes are BusinessRuleViolationException conflicts.

## Explicit usual-price handling

- Create requires an item, merchant, explicit currency and amount. It is not an
  upsert: a duplicate item/merchant/currency/unit price returns a domain conflict
  and must be edited with its revision. A create never overwrites a remembered
  price implicitly.
- FindUsualPriceAsync checks item/merchant household scope, normalizes currency and
  returns only an exact item/merchant/currency/unit match. No match returns null;
  it does not fall back to another shop, currency, pack or catalogue amount.
- UpdateUsualPriceAsync requires a nonempty expected revision. A stale revision
  produces SpendingPriceConflictException. An explicit update, even to the same
  value or to zero, advances the timestamp/revision. Invalid amounts leave the
  saved price and revision unchanged.
- Item, merchant, unit and currency are not editable on an existing price. Create
  another price identity when a different combination is intended.
- Currency and price precision/range validation are unchanged from the first
  slice: currency shape is validated; receipt minor-unit policy remains deferred.

## Transactions and concurrency

All mutations use the existing SerializableTransaction helper: a fresh context
and serializable transaction per attempt, with bounded retry only for guaranteed
serialization/deadlock rollback. Failed validation/save never persists partial
metadata edits. Cancellation flows through EF and transaction operations.

Creation checks normalized code/price uniqueness and also translates the matching
PostgreSQL unique-constraint violations into BusinessRuleViolationException, so
concurrent creates cannot leak a raw uniqueness error or persist duplicates.
The usual-price index has the explicit stable name
UX_SpendingPrices_ItemMerchantCurrencyUnit, shared by its mapping and exception
translation. Unrelated database errors are not reclassified as conflicts.

Usual-price edits check the caller's expected revision inside every transaction
attempt; a retry cannot silently adopt the newer revision. The EF concurrency
token protects the subsequent write too, with DbUpdateConcurrencyException mapped
to SpendingPriceConflictException. Category, merchant and item metadata follow
existing setup-service conventions without new revision columns or request hashes.

Metadata edits explicitly use last-writer-wins semantics. Serializable transactions
protect one server operation, not a form loaded before another edit. UpdateItemAsync
submits name/category/label/notes together, so a stale form can overwrite intervening
changes to those fields, including after a transaction retry. This is accepted for
the current setup service convention; it is not stale-form detection. Before UI
editing is introduced, expose that behaviour clearly or deliberately introduce
expected metadata revisions with stale-form tests. Usual-price revisions remain
mandatory and independent of these metadata semantics.

## Verification

The 29 new PostgreSQL service cases pass. Coverage includes scoped creation/editing/inventory,
empty versus missing inventories, reversible retirement and explicit retired use,
price lookup isolation, normalized duplicates, invalid-input rollback, failure at
database save, cancellation, concurrent duplicate item/price creates and concurrent
price updates with exactly one winner. Existing domain/persistence tests remain
separate and continue to protect database-level constraints.

At completion of the initial service slice, full backend verification passed:
984 tests (650 data/domain and 334 API), with no
failures or skipped tests. The run builds all backend projects and checks the
existing migration/model consistency tests. Cached NuGet metadata still emits
NU1900 for the unavailable vulnerability feed; no dependencies were added.

The [setup API](spending-setup-api-slice.md), including 409 mapping for stale
usual-price revisions, is implemented, as is [record domain/persistence](spending-records-slice.md).
[Record services](spending-record-service-slice.md) are also implemented; record HTTP
capture is next. UI remains deferred.
