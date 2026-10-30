# Full snapshot summary

The first reporting slice calculates an overview of one saved snapshot on demand.
It creates no summary tables, migrations or stored documents. Individual holdings
and household allocation reports remain subsequent slices.

## Contract

`SnapshotSummaryService.GetAsync(householdId, snapshotId, cancellationToken)` returns
a `SnapshotSummary`. The header contains the saved household name, date/precision,
revision and creation/update timestamps. The saved person roster includes people
with no holdings, using both their original person IDs and copied snapshot-person
IDs. Ownership and allocation references use the latter.

The hierarchy is currency, category, group, then ordinary items and financed
assets. Totals at each level are named `AllRecordedTotals`: excluded holdings are
included in this overview, so these figures are not household net worth. Categories
and groups are distinguished by IDs, not their labels. Only groups and currencies
present in saved entries appear; current setup cannot introduce inventory into an
old summary. An empty snapshot returns its header/roster and an empty currency list.

Each entry exposes its original and saved entry IDs, name, country, inclusion flag,
ownership/holder and household allocations. Financed entries also expose purchase
information, original borrowing, withdrawal facility, loan holders, all three
balances and derived amounts. Currency/classification come from the parent groups.
Every balance exposes its origin, last confirmation date/precision and immediate
carry-source entry ID. Missing, confirmed zero and carried zero remain distinct.

Response records live under `Summaries/Models`. They contain values and references,
not tracked EF entities or navigation cycles. No finance HTTP endpoints are added.

## Calculation rules

Each currency/category/group has three subtotals:

- Ordinary assets: sum of known ordinary amounts in Asset categories.
- Ordinary debts: positive sum of known ordinary amounts in Debt categories.
- Financed-asset equity: sum of known valuation minus outstanding loan balance.

`KnownNetPosition = OrdinaryAssets - OrdinaryDebts + FinancedAssetEquity`.
There is no total across currencies and no currency conversion or rounding.
Purchase price, original loan and withdrawable funds are informative; none is
added to equity. Negative equity and negative net positions remain negative.

Each subtotal has `KnownSubtotal`, `MissingEntryCount`,
`CarriedForwardEntryCount` and `IsComplete`. Counts refer to contributing entries,
not balance fields. An asset missing one or both equity inputs counts as one
missing equity entry and contributes no equity to the known subtotal. Its known
balance remains visible in the detail. Missing withdrawal funds do not make equity
incomplete. With no contributing entries a subtotal is complete and zero.

Carried values count normally. A known financed equity contribution counts as
carried if either its valuation or outstanding debt is carried, once even if both
are carried. A carried withdrawal balance alone does not count as carried equity.
An unknown equity contribution counts as missing rather than as a known carry;
its individual balances still show their provenance. Completeness propagates up
the hierarchy, independently of carry status. Missing entries are not estimates
and a known subtotal is not necessarily a lower bound (missing debt can reduce it).

Decimal overflow throws rather than clamping or returning a misleading partial
figure. This includes the existing derived financed-asset calculations. All
recorded balances remain available through the snapshot reader for correction.
Net positions are calculated and validated when each totals object is constructed,
so overflow at group, category or currency level fails the service request before
returning a response. Reading or serializing a returned net position performs no
further arithmetic. Totals expose get-only properties to keep their components and
calculated net position consistent.

## Worked example and independent expectations

The integration example records these synthetic holdings:

| Currency | Entry | Amount or components | Contribution |
| --- | --- | --- | --- |
| ZAR | Person A pension | 200,000 | +200,000 |
| ZAR | Emergency savings held by Person B | 100,000; household allocation 60/40 | +100,000 |
| ZAR | Child savings, excluded from household | 10,000 | +10,000 |
| ZAR | Ordinary loan | 40,000 | -40,000 |
| ZAR | Shared home | Valuation 2,000,000; debt 1,200,000; withdrawal missing | +800,000 |
| GBP | ISA | 5,000 | +5,000 |

Expected ZAR ordinary assets are 310,000, ordinary debts 40,000 and financed
equity 800,000: the all-recorded net position is **1,070,000 ZAR**. Separately,
the GBP net position is **5,000 GBP**. Savings total **110,000 ZAR**; the debt
category contributes **-40,000 ZAR**. All totals are complete despite the missing
withdrawal balance. No ownership or household split changes these full values.

Additional cases exercise missing asset/debt values, incomplete financed entries,
zero and negative equity, exact tiny amounts, carry provenance/counts, saved
classification/ownership after live changes and retirement, corrections/revisions,
duplicate names, empty snapshots, household boundaries, cancellation and overflow.
Allocation assertions resolve shares to the intended people. Boundary cases cover
net-position overflow at each aggregation level and successful JSON serialization
at the largest representable net position.

## Read consistency and scope

The service delegates loading to the existing `SnapshotService.GetAsync`, which
uses a RepeatableRead transaction around its split queries. A pure internal
calculator projects that complete graph and rolls up entries without database
access. It reads no current setup, writes no rows and maintains no cache. A later
request sees the latest committed correction; an already returned summary remains
an independent result with its original revision.

Future person/household reports can reuse the entry projections and aggregation
rules, but this slice does not apply household shares or infer personal loan
responsibility from named loan holders. Historical imports, FX, time series,
stored summaries, frontend and API contracts remain separate work.

## Verification

The Docker test-image build passes with zero warnings/errors. All
350 PostgreSQL integration cases pass, including 20 summary cases. Tests use
disposable databases. No migration is added, and the running API is not rebuilt
or deployed as part of this slice. Changes remain uncommitted for review.
