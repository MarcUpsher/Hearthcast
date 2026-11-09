# Hearthcast

Your household finances, now and ahead.

A household finance app built around configured items and dated snapshots.

## What it does

- Configure households, people, categories/groups, tracked items and financed assets,
  including individual ownership and household allocations.
- Record dated snapshots in bulk, carry values forward, explicitly add missing
  items, and correct saved readings without rewriting their historical metadata.
- Browse full, individual and household summaries, with saved exchange rates,
  merged currency views, completeness indicators and expandable holdings.
- Explore household history with date ranges, calendar intervals, currency choices
  and category/person graph lines.
- Model investments, savings and multiple loans from a selected snapshot. Save
  reusable/default presets, compare nominal and real values, inspect growth and
  loan payoff breakdowns, and add an optional expense-coverage target.
- Import pre-normalised historical data through the optional command-line tool.

The backend is .NET/EF Core with PostgreSQL; the frontend uses Angular Material.
API, database and web app run separately in Docker Compose. A one-shot migration
service applies the schema before the API starts. Ordinary API startup does not
migrate or seed financial records.

The app is intended for local-only access and currently has no authentication or
sessions. See the [documentation index](docs/index.md) for current behaviour,
contracts, architecture, running/tests and historical implementation notes.

## Run with Docker

Requires Docker Engine and Docker Compose.

On a fresh checkout, copy `.env.example` to `.env` and replace its password with a
long random hex password. Then:

```sh
docker compose up -d --build --wait
```

Readiness: `http://127.0.0.1:5190/health`.
Liveness: `http://127.0.0.1:5190/health/live`.
Frontend: http://localhost:4201. See [frontend development](frontend/README.md).

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

## Development and documentation

Backend source/tests/tools live under `backend/`; the Angular app lives under
`frontend/`. Source builds use the .NET SDK specified in `global.json` and the
frontend dependencies locked in `frontend/package-lock.json`.

- [Documentation index](docs/index.md)
- [Project responsibilities and dependencies](docs/project-layout.md)
- [Current modelling behaviour](docs/modelling.md)
- [Frontend development](frontend/README.md)
- [Running, migrations and backend tests](docs/containers-and-postgres.md)
- [Historical import command](docs/historical-import.md)

Implementation notes are retained in `docs/`. The index distinguishes current
references from earlier slices whose request shapes or UI have been superseded.
