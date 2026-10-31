# HTTP workflow verification

`HouseholdWorkflowApiTests` exercises the complete application journey through
HTTP, with a disposable PostgreSQL database. Only database/test-host lifecycle
uses infrastructure directly; no service calls or database writes create the
scenario's application data. Every POST checks its Location returns the created resource's ID.

The synthetic household has two participants and an excluded child, user-defined
asset/debt categories and groups, pensions, shared-purpose savings, a separate
currency holding, ordinary debt and a financed asset. Its ownership split is
25/75, household split 50/50, and borrower link belongs to one person. Withdrawable
funds are recorded without adding them to equity.

| GBP net position | January | February | Corrected January |
| --- | ---: | ---: | ---: |
| Full overview | 1070 | 1140 | 1050 |
| Alex holdings | 260 | 280 | 220 |
| Morgan holdings | 800 | 850 | 820 |
| Child holdings | 10 | 10 | 10 |
| Household | 1060 | 1130 | 1040 |
| Alex household contribution | 610 | 760 | 612 |
| Morgan household contribution | 450 | 370 | 428 |

The separate ZAR holding remains 5 throughout, with no combined currency total.
All these known values produce complete totals; missing-value coverage lives in
the focused snapshot and summary tests. Calculated totals and completeness flags
are asserted directly in JSON, independently of report-model calculations.

After January, savings allocations change from 60/40 to 100/0, a pension retires,
a replacement fund is added, and a group is renamed. February carries unchanged
balances and records new fund/savings values. Tests check retired-item omission,
replacement inclusion and loan provenance. Dates use month precision and normalize
to the first of the month. A later January correction uses the original saved
allocations, items and labels. It changes January savings from 200 to 220 and the
loan from 40 to 80. February keeps its carried loan amount of 40, provenance,
revision and all other saved data. Actual per-person response subtotals and net
positions reconcile to their aggregate view in every currency. The snapshot
inventory returns newest first.

Run this scenario alone:

```sh
docker compose --profile test run --build --rm tests --filter FullyQualifiedName~HouseholdWorkflowApiTests
```

Run all PostgreSQL-backed tests:

```sh
docker compose --profile test run --build --rm tests
```

Tests create and remove isolated databases. They do not seed the application
database. Existing focused tests retain coverage of validation, conflicts,
missing values, household boundaries and numerical edge cases.
