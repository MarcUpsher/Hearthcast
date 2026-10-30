# Hearthcast

Your household finances, now and ahead.

A household finance app built around configured items and dated snapshots.

## Current step

The persistence model implements household setup, classification, tracked items,
household allocations, financed-asset setup and snapshots, plus ordinary-snapshot capture, carry-forward and corrections.
PostgreSQL is the only database provider, with EF Core migrations and real database integration
tests. A full-snapshot summary service calculates totals by currency, category and
group, with completeness and carry-forward information. Individual/household
reports, finance endpoints and the Angular Material frontend follow in separate steps.

The API and database run in separate Docker containers. A one-shot migration
service applies the schema before the API starts. There is no user data seeded
and no schema mutation during ordinary API startup.

See the [full snapshot summary review guide](docs/snapshot-summary-slice.md) for the latest slice.

## Run with Docker

Requires Docker Engine and Docker Compose.

On a fresh checkout, copy `.env.example` to `.env` and replace its password with a
long random hex password. Then:

```sh
docker compose up -d --build --wait
```

Readiness: `http://127.0.0.1:5190/health`.
Liveness: `http://127.0.0.1:5190/health/live`.
There is no frontend yet.

```sh
docker compose ps -a
docker compose logs --tail=100 api migrate
docker compose --profile test run --build --rm tests
docker compose down
```

The database persists in a named Docker volume. `down` retains it; removing the
volume removes its data. Local `.env` files and financial files are excluded from
Git and the image build context.

See [containers and PostgreSQL](docs/containers-and-postgres.md) for architecture,
migrations, tests, configuration and optional host debugging.

## Model and structure

```text
Hearthcast.slnx
Dockerfile
compose.yaml
backend/
  src/
    Hearthcast.Api/
    Hearthcast.Data/
  tests/
    Hearthcast.Data.Tests/
frontend/                 # Planned Angular Material app; not created yet
```

- [Schema proposal](docs/schema-proposal.md)
- [Relational schema and implementation plan](docs/relational-schema-plan.md)
- [Household and Person slice](docs/household-person-slice.md)
- [Classification slice](docs/classification-slice.md)
- [Tracked items slice](docs/tracked-items-slice.md)
- [Ordinary snapshots slice](docs/ordinary-snapshots-slice.md)
- [Snapshot lifecycle slice](docs/snapshot-lifecycle-slice.md)
- [Full snapshot summary slice](docs/snapshot-summary-slice.md)
- [Synthetic household history specification](docs/household-history-tests.md)
- [Project layout and naming](docs/project-layout.md)

Source builds on the host use the .NET 10 SDK specified in `global.json`.
The container workflow builds and tests with the official .NET 10 SDK image.
