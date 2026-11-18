# Historical implementation brief: recorded spending by category

Original implementation handoff for Luna, 18 November 2026, retained as a historical
brief. The service and API are now implemented; use the completed
[service notes](spending-category-summary-service-slice.md) and
[HTTP contract](spending-category-summary-api-slice.md) for current behaviour.
The instructions below describe the original slice boundary. The user wants
backend layers completed before UI work.

## Outcome and boundary

Implement a read-only service and its tests that answer:

> For this household, between these two dates, how much recorded spending was
> there in each category, separately for each currency?

The first useful result is “Food: GBP 120; Eating Out: GBP 45”. Product detail is
optional during capture; reporting must work equally well with a quick amount,
category splits and fully itemised purchases.

Both dates are required `DateOnly` values, **inclusive**. Accept arbitrary ranges,
including a single day, a week, a fortnight or a calendar month. The future UI
will translate “December” into dates. Do not implement calendar presets or
monthly-only contracts in this backend.

This slice includes the Data-layer service, feature-owned read models, meaningful
PostgreSQL-backed tests and documentation. API endpoints/HTTP DTOs/DI registration
belong to the following slice. No UI, budget comparisons, plan allocation,
proration, merchant/item breakdowns, currency conversion, new capture features or
receipt extraction. No schema change is expected; justify any genuinely necessary
change rather than redesigning the existing model.

## Calculation rules

- Scope every query to the requested household and `SpendingDate` range.
- Exclude voided records. Retired setup entities do not exclude saved spending.
- Sum saved `SpendingLine.NetAmount`. Do not recalculate quantity × unit price,
  use remembered prices, or substitute `ReceiptTotal` for the saved line amounts.
- Payments contribute positively; refunds contribute negatively on the refund's
  own `SpendingDate`. A linked original payment outside the range does not exclude
  the refund. Voiding the original does not itself void a separate refund.
- Group by stable saved `ExpenseCategoryId` and currency. Never combine currencies
  or regroup historical lines through the item's current default category.
- Use copied historical category metadata, not mutable setup names. One category
  may have different saved names across the range. Suggested implementation
  choice: show the most recent saved name within that category/currency group,
  with deterministic tie-breaking. Document and test the chosen policy; a rename
  must not split one category ID into multiple numerical groups.
- Preserve exact decimal amounts and currency precision. Use checked aggregation
  and the existing validation-error conventions for unrepresentable results.
- Use a deterministic row order. Zero net totals are valid, including purchases
  fully offset by refunds; do not confuse them with no captured records.

Receipt reconciliation remains visible. A supplied receipt total differing from
the sum of its lines must produce a warning identifying the record and its saved
date/merchant/currency, receipt total, line total and difference. Emit it once per
record, even when the receipt spans categories. Difference follows the existing
`ReceiptDifference` convention: receipt total minus unsigned line total, including
for refunds. Do not spread the discrepancy among categories or silently adjust
their totals. Only included nonvoid records contribute warnings.

A missing receipt total means **NotChecked**, not reconciled. Keep this distinction
in the read model (for example, record counts by reconciliation status) without
building a large reporting dashboard. Call the result **recorded spending**:
balanced receipts do not establish that every purchase has been captured. A known
household with no eligible records returns an empty report, not a completeness
claim. Report recorded spending whether or not a category has a budget.

## Suggested service shape

Follow the existing feature service pattern using
`IDbContextFactory<HearthcastDbContext>`. A separate
`SpendingCategorySummaryService` under `Hearthcast.Data/Spending/` is a natural fit:

```csharp
Task<SpendingCategorySummary> GetAsync(
    Guid householdId, DateOnly from, DateOnly to,
    CancellationToken cancellationToken = default);
```

The report should echo household/range, contain category/currency net totals,
reconciliation warnings and enough reconciliation information to distinguish
unchecked receipts. Exact type/member names are implementation choices. Avoid
returning mutable tracked aggregates or introducing generic repositories.

Reject default/missing date values and reversed ranges with
`InvalidValueException`. Use existing household validation/not-found conventions;
an unknown household is not an empty known household. There is no clock-derived
default date. No arbitrary monthly restriction or silent range truncation.

Read totals and warnings from one consistent dataset. A single no-tracking query
for the selected records/lines is a reasonable starting point; if multiple data
queries are needed, address consistency under concurrent corrections/voids. Avoid
N+1 queries and loading unrelated history. Never calculate a range total from
only one page of the spending-history API. This operation must not write totals,
change revisions, update usual prices or alter budget state.

## Existing code to inspect

- `backend/src/Hearthcast.Data/Spending/SpendingRecord.cs`: saved header,
  `CountedAmount`, `ReceiptDifference`, `ReconciliationStatus`, void semantics.
- `backend/src/Hearthcast.Data/Spending/SpendingLine.cs`: saved category metadata,
  authoritative `NetAmount`, optional detail.
- `backend/src/Hearthcast.Data/Spending/SpendingRecordService.cs`: factory usage,
  household checks, inclusive date filters, ordered graph reads and capture APIs.
- `backend/src/Hearthcast.Data/Spending/SpendingValueRules.cs` and
  `SpendingCurrencyPolicy.cs`: exact money rules and supported currency metadata.
- `backend/src/Hearthcast.Data/Persistence/HearthcastDbContext.cs` and spending
  configurations: existing relationships, household scope and indexes.
- `backend/tests/Hearthcast.Data.Tests/Spending/SpendingRecordServiceTests.cs`:
  fixtures and real capture/setup operations for test data.
- `backend/tests/Hearthcast.Data.Tests/Infrastructure/PostgresTestDatabase.cs`:
  disposable database lifecycle.
- `docs/spending-records-slice.md`, `spending-record-service-slice.md` and
  `spending-and-budget-schema-plan.md`: existing domain rules and wider roadmap.

The wider roadmap mentions actual-vs-plan reporting. The user explicitly narrowed
this increment to recorded spending totals; budget comparison is still deferred.

## Verification and completion

Cover these behaviours with focused tests rather than mirroring private helpers:

- Inclusive endpoints, an excluded date on either side, a single-day range and
  an arbitrary range crossing a month/year boundary; invalid/default dates.
- Household isolation, missing household and an empty eligible range.
- Quick, split and itemised lines aggregate together. An overridden discounted
  `NetAmount` wins over quantity × unit price. More records than a history page
  still contribute to the full result.
- Separate currencies, multiple categories, repeated category/item lines, saved
  category names after setup rename and saved classification after item changes.
- Refunds in their own range, negative and zero net groups, void exclusion and a
  refund whose linked original is outside the range or later voided.
- Matched, mismatched and absent receipt totals; a split receipt yields one
  warning; warnings and category totals use exactly the same eligible records.
- Exact decimal totals and deterministic labels/order; reads leave saved state
  unchanged. Test multi-query consistency if the design introduces that risk.

For a simple sanity fixture, GBP Food payments of 25 and refund of 5 give Food
20; GBP Eating Out 8 stays separate; EUR Food 7 stays separate; a voided GBP Food
100 contributes nothing. A receipt discrepancy warns without changing these sums.

Run the focused new tests and the appropriate Data test suite. Use the existing
isolated PostgreSQL setup described in `docs/containers-and-postgres.md`; never
target the application database or print `.env` credentials. This machine has
`/tmp/hearthcast-run-local.py`, which supplies the private test connection:

```sh
python3 /tmp/hearthcast-run-local.py dotnet test backend/tests/Hearthcast.Data.Tests/Hearthcast.Data.Tests.csproj --no-restore --filter FullyQualifiedName~SpendingCategorySummary -v minimal
```

Host tests may require the normal sandbox escalation. Use the fixture's disposable
databases. Record what actually passed; do not reuse historical test counts as
proof. Update documentation with the resulting contract and any design choices.

At handoff, the working tree already contains completed, uncommitted budget
snapshot API work and its documentation, including the `TimeProvider` review
follow-up. Preserve it. `review.md` is intentionally ignored. Inspect current
status before editing; do not stage/commit/push unrelated changes. This handoff
does not request a commit or push. Finish with changed files, verified behaviour,
test results and any material limitations.
