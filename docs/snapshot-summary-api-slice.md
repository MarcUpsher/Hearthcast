# Full snapshot summary HTTP API

GET `/api/households/{householdId}/snapshots/{snapshotId}/summary` returns 200
with the calculated overview of every recorded holding in that saved snapshot.
It uses `SnapshotSummaryService` and its immutable report models as the response
contract. These contain no EF navigation graphs.

The report includes the saved roster and reporting context, snapshot revision,
currencies, categories, groups, item contributions and financed-asset balances.
Each currency, category and group has `allRecordedTotals`: ordinary assets,
ordinary debts, financed-asset equity and known net position. Debt subtotals are
positive and are subtracted once. Available-to-withdraw balances are reported
as context; they are not an additional asset on top of equity.

The full report includes holdings excluded from household finances. The household
contribution report is a separate view for a later endpoint. Currencies remain
separate; no combined currency total or FX conversion is performed.

Unknown values contribute a missing-entry count and make the relevant totals
incomplete. Known subtotals remain visible. Carried contributions are counted and
retain their provenance. An empty capture keeps its saved roster and returns an
empty currencies collection.

Reports are calculated on demand from a consistent read of the saved snapshot.
Corrections appear on the next GET with the current revision. Setup edits do not
change saved category/group/person metadata. GET does not save summary tables.
Missing snapshots, missing households and snapshots belonging to another household
return the existing 404 problem response with a trace ID. Cancellation is passed
through to the existing service. There are no new tables or migrations.

PostgreSQL-backed HTTP tests cover known arithmetic, separate GBP/ZAR totals,
excluded holdings, missing values, equity, category/group breakdowns, corrected
values and revisions, carried counts, preserved history, empty captures and
household scope.

The summary HTTP tests use separate cases for initial totals, corrections and saved
metadata, carry-forward, and empty captures/household scope. JSON assertions check
serialized net totals and completeness flags directly, so deserializing into the
report models cannot mask missing or incorrect calculated response fields.
