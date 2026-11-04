# Merged snapshot summary

`GET /api/households/{householdId}/snapshots/{snapshotId}/merged-summary` calculates
a read-only summary from one consistent snapshot read. No summary tables or new
financial records are stored. The response includes the snapshot ID/revision,
all currencies present in the snapshot, household totals and per-person household
contributions, per-person individual holdings, and all recorded balances.

The service reads the saved snapshot and delegates to `MergedSnapshotSummaryCalculator`.
The calculator reuses the existing scope calculators. Actual holdings remain distinct
from household allocations. Ordinary debts subtract from the net position, while
financed assets contribute attributed equity (valuation minus outstanding loan).
Withdrawable funds are not added to equity again. Excluded holdings appear only
in their appropriate individual/all-recorded views.

Categories and groups merge across source currencies, using their IDs and saved
names (and category financial type) so differing historical labels remain distinct.
Each entry retains its native contribution, status and copied metadata. Every row
has one amount for each currency represented anywhere in the snapshot, even if
that currency has no native holdings in the selected scope.

Conversions use only the snapshot's saved base-to-quote rates. A source value is
multiplied by target-base-rate / source-base-rate (the base currency has rate 1).
Same-currency values need no rate. Decimal arithmetic stays on the backend;
amounts are rounded only for display. Parent totals sum converted child values,
without adding independently rounded display amounts.

Carried-entry counts use a calculation flag independent of the displayed status text.
Each currency cell returns a nullable known subtotal plus missing-value,
missing-rate and carried-entry counts. A wholly unavailable amount is null;
a partially calculable total contains its known subtotal and is flagged incomplete.
Empty scopes total zero. Recorded zero and unknown remain distinct. Completeness
only describes the recorded scope, not whether a historic snapshot covered every
real-world account. Out-of-range decimal results return a validation error.

The frontend displays the combined summary above the native-currency blocks.
Categories start open and groups start collapsed. Items show native balances,
converted contributions, status and institution/fund metadata. View/person controls
apply to both summary presentations; the recorded-entry filters remain separate.
Revision checks prevent displaying rates and reports from different snapshot
revisions if a concurrent edit or enrichment occurs during loading.
