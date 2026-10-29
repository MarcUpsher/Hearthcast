# Containers and PostgreSQL

Hearthcast now uses PostgreSQL only. Docker Compose runs the database, a one-shot
migration process and the API. The optional test service uses the same PostgreSQL
engine with a separate generated database for each integration case.

## Start the app

Requires Docker Engine and Docker Compose. No host .NET SDK or PostgreSQL
installation is required for the container workflow.

1. Copy `.env.example` to `.env` on a fresh checkout.
2. Replace the example password with a long random hex password. The local `.env`
   is ignored by Git and excluded from the image build context. A private local
   password has already been generated on the development machine.
3. From the repository root, run:

```sh
docker compose up -d --build --wait
```

The API is available at `http://127.0.0.1:5190/health`. There are no finance API
endpoints or frontend yet; the current application surface is health reporting.

```sh
docker compose ps -a
docker compose logs --tail=100 api migrate
docker compose down
```

`down` removes the project's containers/network while retaining its named data
volume. Do not add `--volumes` when you intend to retain the database.

The API and database ports bind to localhost. `.env` can override API_PORT and
POSTGRES_PORT if another application uses the defaults. Container-to-container
connections always use `postgres:5432`, independently of the published host port.

## Services and persistence

| Service | Purpose |
| --- | --- |
| postgres | PostgreSQL 18.6, persistent `postgres-data` volume, readiness check. |
| migrate | Runs the API image with `--migrate`, applies EF migrations and exits. |
| api | Non-root .NET runtime image; starts after migration succeeds. |
| tests | Optional SDK image; runs integration tests and exits. |

The PostgreSQL 18 image stores its data beneath `/var/lib/postgresql`; that is the
volume mount, following the [official image guidance](https://hub.docker.com/_/postgres).
Stopping/recreating containers or rebuilding the API preserves the volume.
Persistence is separate from backups. Updating POSTGRES_PASSWORD in `.env` does
not change an existing database role's password; initialization settings apply to
an empty volume. PostgreSQL major upgrades also require a deliberate upgrade
procedure, rather than simply changing the major image tag over existing data.

The API image uses a multi-stage build: restore/publish with the official .NET 10
SDK, then copy published output into the ASP.NET Core 10 runtime image. Runtime
runs as the image's non-root app user. curl is included for its Docker health
check; source, SDK, tests and local credentials are absent from the runtime image.
The official `.NET 10.0` image tags are used because the host's exact SDK patch
has no published matching image. Host builds still use `global.json`.

The build context uses an allowlist in `.dockerignore`, restricted to the backend
source/tests and build definitions, with local configuration and generated output
excluded. It does not send the working directory's financial files or Git history
to the builder.

This is a local development stack. The database role also supports migrations and
creating isolated test databases; it is not a separate production deployment or
a production role/credential configuration.

## Migrations and readiness

The explicit migration process runs before the API during Compose startup. A
failed migration prevents API startup. The ordinary API process does not create,
seed or migrate the database. The migration process is also callable separately:

```sh
docker compose run --rm migrate
```

- `/health/live`: confirms that the HTTP process is running, without database access.
- `/health`: checks database connectivity and that all known migrations are applied;
  returns HTTP 503 when the database is unavailable or migrations are pending.
- Docker monitors `/health`, so an API that cannot use its database is unhealthy.

Health responses expose a simple status, rather than connection details. EF
sensitive-data logging is not enabled.

The four pre-release SQLite migrations are replaced with one PostgreSQL baseline,
CreateInitialPostgresSchema. There was no Hearthcast application database to
transfer. This is **not** a SQLite data-conversion tool. The SQLite checkpoint is
retained in Git at `664b27e`. Subsequent PostgreSQL schema changes use ordinary
incremental EF migrations.

## PostgreSQL mapping and transactions

The logical entities and household rules remain the same. The provider mapping
now uses UUID identifiers, native booleans, date-only financial dates and UTC
`timestamp with time zone` timestamps. Monetary amounts use unconstrained native
`numeric`, preserving .NET decimal values without a fixed-scale rounding rule or
text converter. Format checks use PostgreSQL regular expressions. Amount checks
reject negative and non-finite numeric values, even through direct SQL.

The existing serializable workflows now share SerializableTransaction. PostgreSQL
can abort a serializable transaction when concurrent writes conflict. The helper
retries serialization failures/deadlocks up to five attempts, creating a fresh
context and rereading state for the entire operation each time. It does not retry
validation errors or ambiguous network/commit failures. See PostgreSQL's
[transaction isolation documentation](https://www.postgresql.org/docs/current/transaction-iso.html).

## Integration tests

```sh
docker compose --profile test run --build --rm tests
```

The test service starts PostgreSQL if needed. Each case creates a uniquely named
`hearthcast_test_...` database, applies real EF migrations, and drops that database
afterwards. Tests never reset the application's `hearthcast` database. They fail
with an actionable message when no test connection is configured, rather than
silently falling back to another provider.

The previous three incremental SQLite-upgrade tests are removed because that
provider's migration chain no longer exists. The remaining migration test checks
the PostgreSQL model, applied migrations, rollback to zero and reapplication. New
coverage checks native numeric aggregation, invalid numeric values and complete
transaction retries. Existing domain, FK, uniqueness, history and rollback tests
run against PostgreSQL.

## Verified locally

- API and test images build successfully; container test build has no warnings/errors.
- All 141 integration cases pass inside Docker against PostgreSQL 18.6.
- The migration service exits successfully before the API becomes healthy.
- The API runs as non-root UID 1654.
- Stopping PostgreSQL makes readiness return 503 while liveness stays 200.
- Recreating PostgreSQL retains the applied migration in the named volume; the
  API reconnects without being rebuilt.
- The test suite leaves no generated test databases behind.

## Optional host development

For VSCode debugging, start only the database:

```sh
docker compose up -d --wait postgres
```

Set `ConnectionStrings__Hearthcast` in your terminal to a PostgreSQL connection
string using `Host=127.0.0.1`, the published POSTGRES_PORT, database/user
`hearthcast`, and the private password in `.env`. Then:

```sh
dotnet restore Hearthcast.slnx
dotnet run --project backend/src/Hearthcast.Api -- --migrate
dotnet run --project backend/src/Hearthcast.Api
```

For host test runs, set HEARTHCAST_TEST_POSTGRES to a connection with database
`postgres` and CREATEDB permission before `dotnet test`. Compose is the simpler
route and requires no host connection-string setup.

For new migrations, restore the local dotnet tool and target the data project:

```sh
dotnet tool restore
dotnet ef migrations add <DescriptiveName> --project backend/src/Hearthcast.Data --startup-project backend/src/Hearthcast.Api --output-dir Persistence/Migrations
```

Generating a migration needs provider metadata but no live database. Applying it
requires the configured connection. Rebuild the image after generating migrations
so the migration service contains the new schema change.
