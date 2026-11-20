# Receipt discounts and last recorded prices

19 November 2026. A receipt can print full item prices and deduct a meal deal or
other offer only at the end. The user can now record that deduction against a
category without inventing discounted prices for each product.

## Amounts and persistence

`SpendingDiscount` belongs to a spending record and saves position, category ID
and copied category name, positive amount, optional description, and the parent's
currency and saved precision. The additive `AddReceiptDiscounts` migration creates
one table; existing receipt lines, usual prices and budget plans are untouched.
Scoped foreign keys and row constraints protect currency, precision, text, amounts
and positions. Aggregate writes require each discounted category to have lines
and the combined discount not to exceed those lines. Zero net spending is valid.

The subsequent [additional-costs slice](spending-additional-costs-slice.md) marks
delivery/handling charges as ordinary categorised lines. GrossAmount continues to
include all lines; purchase and additional-cost subtotals are also exposed. The
new flag preserves versions one and two when absent/false; flagged costs use a
version-three create hash.

- `GrossAmount` is the sum of saved line `NetAmount` values.
- `DiscountAmount` is the sum of receipt discounts.
- `TotalAmount` is gross minus discounts; reconciliation compares this to the
  optional receipt total. A mismatch remains saveable and is never auto-allocated.
- Payments count the net total; refunds negate it on their own recorded dates.
  Voided records count zero and retain their original amounts and discounts.
- Category reporting subtracts the assigned discounts before applying the refund
  sign. It never counts the receipt total as another amount.

Printed unit prices, quantities, authoritative line amounts and promotion flags
remain unchanged. Receipt offers are not allocated among products. Product prices
and their effective line prices therefore do not claim to include receipt offers.
Remembered usual prices never change implicitly.

Full aggregate reads load ordered lines and discounts in separate collection
queries to avoid a line-by-discount Cartesian product. Read-only queries share
one repeatable-read transaction; writes retain serializable transactions.
Category labels use the latest saved record; within one record a saved discount
label takes precedence over a line label, followed by descending position.

## API and retry compatibility

Capture accepts optional `discounts: [{ expenseCategoryId, amount, description }]`.
Responses include ordered discounts, `grossAmount`, `discountAmount` and the net
`totalAmount`. Omitted, null or empty capture discounts retain the exact version-one
create hash contract. Nonempty discounts use version two and include their ordered,
canonical values. A later correction does not change the original create hash.

`PUT /api/households/{householdId}/spending/records/{recordId}/discounts` requires
`expectedRevision` and the complete discount array. It preserves line IDs, saved
line context and usual-price source links. An empty array clears discounts. A
stale revision or voided record conflicts. Invalid edits roll back completely.

Full record replacement preserves discounts when the field is omitted, replaces
them when an array is supplied, clears with `[]`, and rejects explicit null. Line
corrections and replacements must remain compatible with retained discounts.
All discount changes share the existing parent revision and serializable transaction.

The summary warning field `lineTotal` retains its JSON name for compatibility but
now contains the net recorded total after receipt discounts. `difference` remains
receipt total minus that unsigned recorded total, including for refunds. UI labels
say Recorded total. Warnings alone never modify category totals.

## Capture and history

Add receipt discount supports multiple category/amount/optional offer rows. Enter
deductions as positive amounts. For the Waitrose example, gross £21.95 minus a
£2.10 Food discount labelled “£8 Stir Fry Dine In” gives £19.85 recorded spending.
The form and history show gross, discounts, net and receipt reconciliation
separately. Discount-only edits use the narrow endpoint; broader edits omit
unchanged discounts so their copied context and identities survive.

Discount drafts participate in validation, dirty guards, currency confirmation,
uncertain-create locking and exact retries. No historical application receipt is
corrected automatically by deploying this feature.

## Last recorded item price

Selecting a catalogue item now also requests its last eligible price for the
exact household, merchant, currency and stable unit, at or before the purchase
date. An edited receipt excludes itself. The UI shows price, date and unit, with an explicit **Use this price** action. When a category has
a receipt discount, the reference explains that the offer was not allocated to
the individual item's price (`hasReceiptDiscount` in the lookup response).

A saved usual price continues to prefill automatically and takes priority. The
subsequent [price-prefill fix](spending-price-prefill-fix.md) uses the last recorded
nominal unit price automatically when no usual price is found or that lookup fails,
with a visible suggestion label. It never creates or replaces a saved usual price. The explicit action
counts as a manual price choice, so late usual-price responses cannot overwrite
it, and a manual line-total override is preserved. Missing/failed historical
lookups leave manual entry available. Date, merchant, currency, item, household
and request-generation guards reject stale results and clear stale references.

## Verification

New PostgreSQL and HTTP cases cover net totals, category caps, precision/scoping,
refunds/voids, migration round trips, legacy retries, source preservation and
omitted/empty/null replacement behaviour. Rendered Material tests cover discount
entry, invalid values, correction routing, retry/dirty guards and last-price races.
Astra reviewed once and found two issues: opening an edit did not start price
lookups, and joining sibling collections multiplied read rows. Sol fixed both
without a second review pass. New regression cases open/reload an existing item
without changing its saved amounts, and concurrently correct discounts between
split queries for detail, history, inventory and summary reads. Each read retains
its original snapshot; SQL does not join lines and discounts together.

After the fixes, all **1,030 Data tests**, **682 API tests** and **617 frontend
tests** pass with no failures or skips, and the production build passes. The
initial Data run encountered an unrelated
concurrency-barrier timing assertion; its isolated rerun and both subsequent full
Data runs passed. No change was made to that unrelated test. NuGet reported the
existing NU1900 warning for unavailable vulnerability-feed metadata; no dependencies
were added.

The local API and web app are deployed, with PostgreSQL and API health checks
passing. A private custom-format database backup was taken before migration.
Read-only rollout checks verified hashes/counts for all 20 pre-existing application
tables were unchanged, the new discount table was empty, the migration was applied,
and the exact tested feature bundles were served. Existing receipt detail/history
and category reports remained readable; the live Organic Bananas lookup returned
£1.95 for Waitrose. No receipts, prices or budgets were edited by these checks.

The existing Waitrose receipt remains unchanged for the user to correct explicitly:
Edit purchase → Add receipt discount → Food → 2.10, optionally entering the offer
label. This changes recorded category spending to £19.85 while retaining its
printed item amounts.
