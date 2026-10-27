# Hearthcast

Your household finances, now and ahead.

A household finance app built around configured items and dated snapshots.

## Current step

A fresh .NET 10 backend shell with a health endpoint. Entity mapping is the next
step, starting with Household and Person. Persistence, migrations and the
frontend will follow in separate steps.

The proposed data model is documented in [the schema proposal](docs/schema-proposal.md).

## Structure

```text
Hearthcast.slnx
backend/
  Hearthcast.Api/
frontend/                 # Planned Angular Material app; not created yet
```

## Build and run

Requires the .NET 10 SDK specified in `global.json`.

```sh
dotnet restore Hearthcast.slnx
dotnet build Hearthcast.slnx --no-restore
dotnet run --project backend/Hearthcast.Api
```

The local launch profile listens at `http://127.0.0.1:5190` without opening a
browser. `GET /health` returns `Healthy` when the application is running.

Stop the API with Ctrl+C. To use another port:

```sh
dotnet run --project backend/Hearthcast.Api --no-launch-profile --urls http://127.0.0.1:5191
```

Keep financial files and local configuration outside tracked source. The root
`.gitignore` excludes build output, local credentials, SQLite files and financial
imports/exports.
