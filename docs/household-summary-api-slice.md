# Household contribution summary HTTP API

GET `/api/households/{householdId}/snapshots/{snapshotId}/household-summary`
returns 200 with household totals and each saved person's allocated contribution,
grouped by currency, category and group. It exposes `HouseholdSummaryService`
and its immutable report models, calculated on demand from a consistent snapshot
read. No schema changes, stored summaries or caching are introduced.

Only holdings included in household finances contribute. Each holding counts once
in household totals; per-person amounts follow saved household allocation shares,
independently of actual holders, ownership shares or borrower links. All saved
people remain visible, with empty currency collections where appropriate.

Item detail contains the full saved `item` and its `contributingAmount`. Financed
asset detail contains the full `asset` and its `attributedEquity`. Only the
contribution is counted in the enclosing scope; do not add the repeated full
balances again. Debt subtotals are positive and subtracted from net position.
Currencies remain separate, with no FX conversion.

Unknown contributions make relevant totals incomplete. Known subtotals and
carried-entry counts remain visible. Shared missing/carried contributions may
appear for multiple people, so counts must not be summed to reproduce household
counts. Monetary contributions reconcile to household totals by currency.

Corrections appear on the next GET with a new revision. Setup edits do not rewrite
saved reporting context. Missing or foreign-household snapshots return 404
ProblemDetails with a trace ID. Cancellation passes to the existing service.
Household scoping follows the existing API; authentication remains future work.

PostgreSQL-backed HTTP tests cover different ownership/household splits, full
household values counted once, excluded holdings, empty people, currency
separation, debt allocations, missing values, corrections, preserved history,
carry-forward and household scope. Net positions and completeness flags are
checked directly in response JSON.
