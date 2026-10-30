# Household summaries

`HouseholdSummaryService.GetAsync(householdId, snapshotId, cancellationToken)` returns
a `SnapshotHouseholdSummary`: the saved header, included household totals by
currency/category/group, and the full saved person roster with allocated household
contributions. It calculates on demand without adding tables, migrations or caches.

## Scope and attribution

Only entries whose saved `IncludeInHousehold` is true contribute. Full values are
counted once for the household. The person breakdown uses saved household allocation
shares, independently of actual holders, ownership shares or loan holders. For
example, savings held entirely by Person B can contribute 60% to A and 40% to B.
Financed equity owned 25%/75% can contribute 50%/50% to household finances.

Excluded entries do not appear in the report or affect completeness. They are
filtered before projecting derived amounts, so even an excluded asset's overflowing
derived calculation cannot block this report. They remain accessible in the full
and individual holdings views. No current inclusion/allocation setting is consulted.

Every saved person remains visible. People with no allocated contributions have an
empty currency list, whether or not they participate in household finances. Groups,
categories and currencies without included entries are omitted. Included missing
or zero contributions still appear. IDs distinguish equal labels.

The top-level `Currencies` and each person's `Currencies` use the same structure.
`Totals` always describes its enclosing scope: the whole household or that person's
household contribution. These are alternative breakdowns, not additive reports.
No total across currencies is calculated.

Each ordinary detail has the full saved `Item`, `AllocationShareBasisPoints` and
`ContributingAmount`. Financed details have the full saved `Asset`, allocation
share and `AttributedEquity`. At household level the share is 10,000 (100%) and the
contribution is the whole known value/equity. Under a person it is their saved share
and attributed amount. Only these contribution amounts feed totals; full nested
balances are retained for inspection and must not be counted again. Actual holders,
owners, loan holders and saved allocations remain visible in those details.

## Totals and provenance

The existing aggregation rules apply: ordinary assets minus ordinary debts plus
financed equity. Borrowing principal, purchase price and withdrawable funds do not
add to equity. Missing valuation or debt makes equity incomplete; missing withdrawal
funds does not. Negative equity stays negative.

Known carried amounts contribute normally, with confirmation/source metadata intact.
Equity is carried if either valuation or debt is carried; carried withdrawal alone
does not mark equity as carried. Missing/carried counts refer to entries within
the enclosing scope. A shared missing entry appears once in household counts and
under every recipient's personal counts, so these counts are not additive across
people. Known monetary contributions reconcile by currency at the supported decimal
precision. Zero shares are represented by absence from the saved allocation list.

`SnapshotShareAllocator` reuses the reviewed ownership allocation algorithm for
ordinary amounts and financed equity. It preserves exact decimal units, distributes
unrepresentable fractional remainders deterministically by largest remainder and
original-person-ID ties, and restores negative equity's sign afterward. No rounding
to currency minor units occurs. Individual ownership attribution now calls the
same helper with ownership shares; its behaviour is unchanged.

Overflow in any included component, household total or personal contribution total
fails the request; the response is not returned partially. Other report views do
not have to be calculated first.

## Worked example

Synthetic ZAR records:

| Entry | Full value | Household allocation A/B |
| --- | --- | --- |
| A's pension | 100 | 100% / 0% |
| B's savings | 200 | 60% / 40% |
| A's ordinary debt | 40 | 25% / 75% |
| Home equity (valuation 1,000 less debt 200) | 800 | 50% / 50% |
| Child savings | 10 | Excluded |

Household ordinary assets total 300, ordinary debts 40 and equity 800, yielding
**1,060 ZAR**. A's contribution is **610 ZAR** (100 + 120 - 10 + 400), and B's is
**450 ZAR** (80 - 30 + 400). The full-record view includes the child's 10 and totals
1,070; the individual holdings view uses actual ownership instead of these shares.
An ISA worth 5 GBP, held by A but allocated entirely to B, appears as a separate
household currency and contributes 5 GBP under B. The child remains in the roster
with no household contributions.

## Implementation and review

The service uses the existing RepeatableRead snapshot reader. The calculator takes
the saved snapshot and itself invokes the shared projector with
`includedInHouseholdOnly: true`, so callers cannot accidentally pass an unfiltered
report projection. The pure calculator allocates each
included entry once, then builds household and person hierarchies from the same
projected graph. Each response contains one coherent saved revision, financial date
and roster. Current setup edits and retirement cannot rewrite old summaries;
corrections update amounts using the saved inclusion and shares. No database writes
are performed by reporting, and no finance HTTP endpoints are introduced.

Tests cover the worked example and component reconciliation, independent holding/
allocation treatment, shared assets and debts, negative/zero/tiny/maximum equity,
excluded invalid or missing values, missing and carried contributions, corrections,
live inclusion/allocation changes, retirement, empty scopes, overflow, serialization,
household boundaries and cancellation. Existing individual and full-summary tests
also exercise the shared allocator and projector after the refactor.

Review coverage separates inclusion changes from retirement. An active item removed
from household finances remains in the next snapshot but disappears from its household
report. Previously excluded ordinary and financed entries can be included in a new
snapshot without altering old reports, even when the old values are subsequently
corrected. A separate retirement case verifies that old reports retain their entries
while subsequent captures omit the retired items and assets.

Changes stay local for review. No migration, running-API deployment, import, FX or
frontend work is included.

Verification: the full PostgreSQL integration run passes all 385 cases, including
17 new household cases. The Docker test-image build passes with zero warnings/errors. Tests
create and remove isolated databases rather than populating the application database.
