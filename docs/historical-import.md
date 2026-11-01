# Historical import command

`backend/tools/Hearthcast.HistoryImport` is a one-off console loader for an already
reconciled version 2 historical JSON file and its independently prepared summary
totals. Financial inputs and generated reports stay outside the repository.
There is no HTTP endpoint or frontend component.

The tool requires an existing migrated PostgreSQL schema and an empty application
database. It refuses a database containing any household, including a second run.
It does not delete data, merge households, infer historical transitions, or apply
migrations. Use the ordinary migration command to prepare an empty database first.

```sh
# Set HEARTHCAST_IMPORT_POSTGRES using your local secret configuration.
dotnet run --project backend/tools/Hearthcast.HistoryImport -- \
  /path/to/history-v2.json /path/to/summary-totals.json
```

The connection string is read from the environment, never a command-line argument.
Output identifies the target host/database and, on success, the new household ID,
snapshot date range, entry counts and number of verified currency/month totals.

## Loading and verification

1. Deserialize the prepared tables with exact decimal amounts. Construct household,
   people, classification, items and financed assets through the existing domain.
2. Construct snapshots chronologically with exactly their declared membership,
   names, effective ownership/household shares and carry sources. All financial
   decisions must already be represented in the file.
3. Restore the registry names, default allocations and retirement flags specified
   in setup. Retirement flags control future checklist selection, not saved history.
4. In one serializable transaction, lock household setup writes, check emptiness
   and save the complete graph. Read the rows back from PostgreSQL and check
   entries/provenance and the three existing summary calculators against supplied
   totals. Commit only when all comparisons pass. Cancellation/failure before
   commit rolls back the graph. A network failure during commit may leave its
   outcome unknown; inspect the database before retrying.

The expected totals file contains one record per snapshot/currency, including
all-recorded, household, excluded, individual ownership, household allocation,
category/group totals and carried-entry counts. The JSON records used by the
loader are defined in `HistoryFile.cs`. Version 2 expresses financed balances as
three rows per asset/snapshot: valuation, outstanding loan, and withdrawable funds.
Value rows explicitly state their origin, confirmation date/precision and optional
carry source. No blank means an inferred sale, zero, or carry.

The application data project grants internal access to this tool so it can select
historical membership within one transaction. This adds no domain methods,
schema changes or application import service. The normal summary services use the
same calculators as the loader's readback verification.

## Tests

The PostgreSQL integration tests use synthetic data and disposable databases. They
cover item and individual financed-balance carries, saved names and allocations,
excluded holdings, registry restoration, duplicate-run refusal and full rollback
on a readback mismatch. Decimal tests reject values that would require rounding.
The prepared private history should also be rehearsed against a disposable database
before running against the application database.

Historical totals reflect the available recorded and carried balances. Before a
holding's first observation, absence is still unknown. Summary completeness only
describes entries included in a snapshot, not proof that all household wealth was
recorded. Currencies remain separate.
