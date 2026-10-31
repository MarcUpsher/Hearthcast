# Individual holdings summary HTTP API

GET `/api/households/{householdId}/snapshots/{snapshotId}/individual-holdings`
returns 200 with every saved person's holdings, grouped by currency, category and
group. It exposes `IndividualHoldingsSummaryService` and its immutable report
models. People with no holdings have an empty currencies collection.

Ordinary items belong fully to their saved holder, including items excluded from
household finances. Financed-asset equity is attributed by saved ownership shares;
only `attributedEquity` contributes to personal totals. The repeated `asset`
details contain full balances for inspection and must not be counted again.
Household allocations and borrower links do not determine personal holdings.

Currencies remain separate. Missing values make relevant totals incomplete;
known subtotals and carried-entry counts remain visible. Reports use saved
metadata, so later setup changes do not rewrite history. Corrections appear on
the next GET with the updated revision. Nothing is cached or persisted as summary
tables, and no schema changes are required.

Missing or foreign-household snapshots return 404 ProblemDetails with a trace ID.
Cancellation passes through to the existing consistent snapshot read. Household
scoping follows the existing API; authentication remains future work.

PostgreSQL-backed HTTP tests cover actual holders versus household allocations,
25/75 ownership versus 50/50 household shares, excluded child holdings, empty
people, GBP/ZAR separation, debt subtraction, missing values, corrections,
preserved context, carries, empty captures and household boundaries. Calculated
net positions and completeness flags are asserted directly against response JSON.
