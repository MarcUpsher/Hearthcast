# Project responsibilities and dependencies

Hearthcast is a feature-oriented modular monolith with a separately served Angular
frontend. Backend source, backend tests, tools and frontend stay clearly separated.
PostgreSQL is the sole database provider; EF migrations retain their generated
names and timestamps.

## Backend

| Location | Responsibility |
| --- | --- |
| `backend/src/Hearthcast.Api/` | REST controllers, request/response contracts, validation/error mapping and health checks. |
| `backend/src/Hearthcast.Data/Households`, `Classification`, `TrackedItems`, `FinancedAssets` | Setup entities, invariants and application operations. |
| `Hearthcast.Data/Snapshots` | Copied historical context, capture, carry-forward, corrections and revision checks. |
| `Hearthcast.Data/Summaries` | Full/individual/household/merged reports and historical series, calculated from saved snapshots. |
| `Hearthcast.Data/Projections` | Preset persistence, snapshot preparation and pure monthly simulation. |
| `Hearthcast.Data/ExchangeRates` | Capture/provider workflows and shared checked conversion using saved quotes. |
| `Hearthcast.Data/Spending` | Expense categories, merchants, catalogue items, remembered prices and setup services; spending-record/line aggregates, currency precision policy, transactional capture and bounded history services. Setup and record HTTP contracts/controllers live under `Hearthcast.Api/Spending`. |
| `Hearthcast.Data/Budgets` | Reusable outgoing defaults and setup services; dated full-plan aggregates, transactional capture/carry/correction, provenance detachment, bounded history and consistent as-of/current-outgoing reads. HTTP endpoints are deferred. |
| `Hearthcast.Data/Persistence` | DbContext, EF configurations, transaction support, design-time tooling and migrations. |
| `backend/tools/Hearthcast.HistoryImport` | One-off prepared historical import; not a frontend workflow. |
| `backend/tests/` | API and data tests; persistence tests use disposable PostgreSQL databases. |

Controllers call feature services. Services enforce household scope, load/write EF
entities and delegate reporting/projection arithmetic to calculators. Reporting
reads saved snapshot context rather than reinterpreting history using current setup.
Projection preparation selects/validates a snapshot, then hands native balances and
saved FX factors to a pure calculator. Reports and projection requests never fetch
new exchange rates or save their calculated output.

The data assembly contains domain, application and persistence code, organised by
feature. There is no generic repository or additional layer between every service
and EF. Shared helpers contain specific reusable rules, not household policy.

## Frontend

| Location | Responsibility |
| --- | --- |
| `frontend/src/app/core/api.ts`, `api-errors.ts` | HTTP transport and shared error formatting. |
| `frontend/src/app/core/workspace.ts` | Household discovery/current selection. The app shell checks the active page's leave policy before a user switches household. |
| `setup/models.ts`, `snapshots/models.ts`, `summaries/models.ts`, `history/models.ts`, `modelling/models.ts` | Feature-owned wire contracts. Snapshot consumers share one complete response shape. |
| `setup/`, `snapshots/`, `history/` | Setup/editing, snapshot entry/browsing and historical graphs. |
| `modelling/modelling.ts` | One owner for assumptions, preset drafts, saves, selection and request/lifecycle coordination. |
| `modelling/chart/` | Pure chart geometry and a presentational chart with local pointer/keyboard state. |
| `modelling/results/` | Presentational expense-target and growth-breakdown panels. No API calls or draft copies. |
| `shared/charts/`, `shared/finance-types.ts` | Shared interpolation and endpoint code types. |
| `frontend/src/testing/` | Generic typed frontend fixtures, excluded from production compilation. |

The browser uses `/api` on the same origin. Nginx serves the built frontend and
proxies to the API. Only the API accesses PostgreSQL. Compose exposes host ports
on loopback; a separate one-shot migration service runs before the API.

## Conventions and checks

- Keep C# types in PascalCase files with matching namespaces/feature folders.
- Keep EF mappings and migrations under Persistence; do not rewrite applied migrations.
- Frontend components keep their template/styles nearby; pure chart helpers have no
  Angular, HTTP or workspace dependencies.
- Wire types preserve backend JSON codes. UI editor state may be nullable while a
  validated request is complete; do not change the backend representation to simplify a type.
- Tests follow feature boundaries. Shared generic fixtures avoid real financial data.
- Keep credentials, backups, imports and local REVIEW.md/review.md handoffs ignored. Build output stays out of Git.

See [running and backend tests](containers-and-postgres.md),
[frontend commands](../frontend/README.md) and the [documentation index](index.md).
