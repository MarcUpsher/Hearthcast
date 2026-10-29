# Hearthcast

Your household finances, now and ahead.

A household finance app built around configured items and dated snapshots.

## Current step

The persistence model implements household setup, classification, tracked items
and household allocations, with EF Core SQLite mappings, migrations and integration
tests. The API still exposes only its health endpoint. Snapshots, financed assets
and the frontend follow in separate steps.

The proposed data model is documented in [the schema proposal](docs/schema-proposal.md).
The [relational schema plan](docs/relational-schema-plan.md) maps the proposed
relationships, constraints and implementation slices for review before entity mapping.
See the [Household and Person slice](docs/household-person-slice.md) for the code
to review and the rules implemented so far.
The [Classification slice](docs/classification-slice.md) adds user-defined
categories/groups and the system Asset/Debt identifier.
The [Tracked items slice](docs/tracked-items-slice.md) connects holders and groups
to reusable items, with independent household allocations and participation guards.

## Structure

```text
Hearthcast.slnx
backend/
  src/
    Hearthcast.Api/
    Hearthcast.Data/
  tests/
    Hearthcast.Data.Tests/
frontend/                 # Planned Angular Material app; not created yet
```

See [project layout and naming](docs/project-layout.md) for the feature folders,
persistence folders and conventions to follow as the app grows.

## Build and run

Requires the .NET 10 SDK specified in `global.json`.

```sh
dotnet restore Hearthcast.slnx
dotnet tool restore
dotnet build Hearthcast.slnx --no-restore
dotnet test Hearthcast.slnx --no-build --no-restore
dotnet run --project backend/src/Hearthcast.Api
```

The local launch profile listens at `http://127.0.0.1:5190` without opening a
browser. `GET /health` returns `Healthy` when the application is running.

Stop the API with Ctrl+C. To use another port:

```sh
dotnet run --project backend/src/Hearthcast.Api --no-launch-profile --urls http://127.0.0.1:5191
```

Keep financial files and local configuration outside tracked source. The root
`.gitignore` excludes build output, local credentials, SQLite files and financial
imports/exports.

Tests apply the migration to isolated temporary SQLite databases and remove them
afterwards. API startup does not create, seed or migrate an application database.
