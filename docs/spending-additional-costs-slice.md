# Additional purchase costs

20 November 2026. Purchases can include delivery, handling and other charges on
top of their products. The VetUK example has £45.98 of products and a £49.97
receipt total, leaving £3.99 to record explicitly as delivery.

## Saved representation and calculations

An additional cost is an ordinary categorised `SpendingLine`, marked with
`IsAdditionalCost = true`. It has a description and a positive actual amount in
the saved receipt currency/precision. Catalogue items, quantities and unit
metadata are excluded. Domain validation and a PostgreSQL check constraint
protect this shape, including during narrow line-amount corrections.

The additive `AddSpendingAdditionalCosts` migration adds a non-null boolean with
default false. Every existing line remains a normal purchase line. No shipping
cost is inferred from a discrepancy or description, and no existing receipt is
corrected automatically. A durable flag lets the UI restore separate sections
without using receipt descriptions or notes as hidden application metadata.

`PurchaseAmount` sums lines without the flag; `AdditionalCostAmount` sums lines
with it. `GrossAmount` retains its previous contract as the sum of all line
amounts. `TotalAmount = PurchaseAmount + AdditionalCostAmount - DiscountAmount`.
Receipt reconciliation compares that net total to the optional receipt total.
Products retain their printed unit prices, quantities and authoritative amounts.

Category summaries already aggregate every saved line once, so a cost counts in
its chosen category without new report arithmetic. Assigning delivery to Pet
Consumables counts the full order as pet spending; choosing a separate Delivery
category reports it separately. Category discounts can reduce those amounts under
the existing category caps. Refunds negate net totals on their recorded dates;
voided records retain charges but count zero. Charges cannot become item-price
sources or participate in last-paid item-price lookup.

## API and compatibility

Capture and full replacement line contracts accept optional `isAdditionalCost`,
default false; responses include the saved flag. Record responses add the two
computed subtotals above. An explicit null or non-boolean flag is invalid.
Classification is retained by header, discount and narrow line corrections;
full replacement supplies explicit line classifications just like other copied
line metadata. Existing revision, household and atomic-save rules apply.

Create requests without flagged costs retain their exact version-one or
version-two hash, including explicit false flags. A fixed independently computed
version-two fixture supplements the existing version-one fixture. A request with
flagged costs uses version three and includes their ordered one-based line
positions, retaining any version-two discount content. Changing a normal line to
a charge therefore changes create intent; an identical retry remains safe after
correction. No old create hashes are recalculated.

## Purchase form and history

Additional costs appears alongside Receipt discounts. Each charge has description,
amount and category controls, plus Remove cost. Multiple charges and different
categories are supported. A single product category is suggested when adding a
cost; otherwise category selection is explicit. Amounts are never filled from
the receipt difference. A blank charge blocks saving until completed or removed.

The form and saved detail show purchase amounts, additional costs, discounts and
the final recorded spending. Charges appear once in their own section when
viewing or reopening a purchase. Adding charges does not require catalogue setup.
They share exact decimal totals, currency-change confirmation, draft guards,
uncertain-create locking, exact retries and edit conflict recovery with other
lines. Existing narrow amount corrections preserve line IDs and price sources;
adding/removing or changing descriptions/categories follows the existing full
replacement workflow and label-refresh notice.

For the existing VetUK purchase: Edit purchase → Add additional cost → Delivery,
3.99, Pet Consumables → Save. This reconciles £45.98 + £3.99 = £49.97.

## Verification

PostgreSQL/HTTP tests cover charges in capture/history/replacement/corrections,
category totals, receipt discounts, refunds/voids, invalid shapes/precision,
database constraints, stale revisions, source preservation, create replay and
migration compatibility. Rendered Material tests cover entry, required fields,
multiple charges, breakdowns, edit routing, history grouping and recovery guards.
Astra reviewed once and found no actionable issues. The focused backend checks
passed (32 Data and 17 API cases, including existing discount/retry regressions).
All 627 frontend tests pass under Europe/London, and production builds pass.

The full API suite passes all 690 tests. The first full Data run passed 1,041
cases and exposed one older budget migration fixture that inserted through the
current EF model after downgrading to a schema without the new column. The fixture
now seeds its historical spending rows through parameterised SQL, preserving the
original migration test and additionally checking the default false flag. No
production change was required for that fixture issue.

The final Data rerun passes all **1,042 tests**, with zero failures or skips.
Together with **690 API** and **627 frontend** tests, all **2,359 tests** pass.
The existing NuGet NU1900 vulnerability-feed warning remains; no dependencies
were added. Production builds and whitespace checks pass.

The local API and web app are deployed, with successful migration and health
checks. A private custom-format database backup was taken before migration.
Read-only rollout checks compared all prior values in 21 application tables,
excluding only the newly introduced flag, and verified they were unchanged.
Every existing line has the new default false value. The exact tested bundles,
spending routes, five receipt contracts and category reports were checked live.
The VetUK purchase remains £45.98 against a £49.97 receipt total, ready for the
user to add the £3.99 charge explicitly. No records were edited by these checks.
