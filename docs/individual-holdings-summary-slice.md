# Individual holdings summaries

`IndividualHoldingsSummaryService.GetAsync(householdId, snapshotId, cancellationToken)`
calculates the personal holdings view of one saved snapshot. It returns a
`SnapshotIndividualHoldingsSummary`, preserving the header, financial date,
precision, revision, timestamps and full saved roster. Nothing is stored or cached.

## Meaning of the view

Each saved person has a currency/category/group hierarchy. Ordinary items appear
in full under their saved holder, whether or not included in household finances.
Financed assets appear only under their saved owners, with equity divided by
ownership basis points. Household allocations and participation do not change
these holdings. A loan-holder link alone does not assign equity or personal debt;
allocating net equity by ownership is a reporting convention, not a legal claim
about each person's borrowing responsibility.

Groups/currencies with no holdings for the person are omitted, but people with no
holdings remain in the roster with an empty currency list. Known zero and missing
holdings still appear. Identity is based on saved IDs, never person/item names.
Each group exposes `HoldingsTotals`, using the same three components and signs as
the full overview: ordinary assets minus ordinary debts plus attributed equity.
Currencies remain separate. No household summary or finance HTTP endpoint is added.

Ordinary item detail reuses `ItemSnapshotSummary`. Financed detail is an
`OwnedFinancedAssetSummary` with:

- `Asset`: the full saved asset detail, including balances and their provenance.
- `OwnershipShareBasisPoints`: this person's saved ownership share.
- `AttributedEquity`: only this amount contributes to the person's totals.

Full valuation/debt/equity in `Asset` must not be counted again beside the
attributed equity. These full details are intentionally repeated when multiple
people own the asset, so each contribution can be inspected.

## Missing values, carries and reconciliation

The same subtotal rules apply as in the full overview. A missing ordinary amount
affects its holder. Unknown financed equity affects every owner, even though known
valuation or debt remains visible. Missing withdrawal funds do not affect equity.
Known attributed equity is carried when either valuation or debt is carried; a
carried withdrawal value alone does not mark equity as carried.

Missing/carried counts describe entries contributing to each person's report.
They must not be summed to reproduce full-overview counts: one shared asset may
appear under two owners. Monetary components do reconcile by currency when fully
known. For incomplete equity, every owner has a missing contribution and the full
overview has one missing asset; both views retain the same known monetary subtotal.

No amounts are rounded to currency minor units. For percentage splits beyond
decimal precision, the ownership allocator works with exact integer coefficients
and chooses a common scale for the total and shares: up to four extra decimal
places for basis points, capped by the decimal scale/range. It divides into units,
then assigns the remaining units to the largest fractional remainders. Ties use
the original person ID (not a newly copied snapshot-person ID or a changeable
name). Negative equity is allocated by magnitude and the sign restored afterward.
This preserves the original total, avoids multiplication overflow and makes equal
inputs stable across captures. At the smallest decimal unit, one equal owner can
receive that unit while another receives zero. Presentation rounding remains
future work.

## Worked expectations

Synthetic ZAR holdings: Person A holds 100 in assets and 40 of ordinary debt;
Person B holds 200 in savings; the child holds 10 excluded from household finances.
A shared asset has valuation 1,000 and debt 200: equity 800, owned 25%/75%.

| Person | Ordinary assets | Ordinary debt | Owned equity | Net holdings |
| --- | --- | --- | --- | --- |
| A | 100 | 40 | 200 | 260 |
| B | 200 | 0 | 600 | 800 |
| Child | 10 | 0 | 0 | 10 |

These reconcile to **1,070 ZAR**. Separate holdings of 5 GBP belong fully to A.
Person B's household savings allocation of 60% to A and 40% to B does not split
those savings in this view. The asset's household 50/50 allocation likewise does
not change its ownership attribution. A fourth person with no holdings remains
visible. A 25% share of equity 100.01 is 25.0025, not rounded to 25.00.

## Implementation and verification

The service loads the saved snapshot once through `SnapshotService.GetAsync`,
inheriting its RepeatableRead consistency and household scoping. The shared
`SnapshotReportProjector` copies and orders saved entry details without aggregating
them. Full-overview and individual calculators then calculate totals independently.
A full-overview aggregate overflow cannot block otherwise valid personal totals;
each report still rejects overflow within its own totals or required entry details.
The pure individual calculator reuses the saved entry projections and shared
`SnapshotSummaryAggregation` helpers. It calculates ownership shares once per
asset, then builds each person's hierarchy. No current setup or live person labels
are queried. Later corrections generate new totals with the saved ownership and
new revision; already returned results remain independent.

Tests cover the worked expectations, per-currency component reconciliation,
full/excluded holdings, empty people, duplicate person names, loan holders without
ownership, negative/zero/tiny/maximum decimal equity, deterministic remainders,
missing/carried inputs, historical ownership and retirement, corrections,
household boundaries, cancellation and empty history. Review cases also cover
multiple remainder units with negative equity and original-person-ID tie-breaking,
an ordinary item's live holder transfer before correcting its old snapshot, and
independent personal totals when only the full aggregate exceeds decimal range.

The host build and Docker test-image build pass with zero warnings/errors. The
full PostgreSQL suite passes all 368 integration cases, including 18 new
individual-holdings cases. Tests create and remove isolated databases.

The new slice stays local for review. No migration, application-database upgrade,
running-API deployment, import or frontend is part of it.
