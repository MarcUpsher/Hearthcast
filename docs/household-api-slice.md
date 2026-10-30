# Household and people REST API

The first finance endpoints expose household and people setup. Controllers handle
HTTP concerns; `HouseholdService` and domain entities continue to enforce the rules.
This slice adds no tables or migrations, authentication, permissions or sessions.

## Routes and contracts

| Method | Route | Request | Success |
| --- | --- | --- | --- |
| POST | `/api/households` | `name`, `firstPersonDisplayName` | 201 HouseholdResponse; Location points to household GET |
| GET | `/api/households/{householdId}` | None | 200 HouseholdResponse including people |
| PUT | `/api/households/{householdId}` | `name` | 200 HouseholdResponse |
| POST | `/api/households/{householdId}/people` | `displayName`, `participatesInHouseholdFinances` | 201 PersonResponse; Location points to person GET |
| GET | `/api/households/{householdId}/people/{personId}` | None | 200 PersonResponse |
| PUT | `/api/households/{householdId}/people/{personId}` | `displayName`, `participatesInHouseholdFinances` | 200 PersonResponse |
| DELETE | `/api/households/{householdId}/people/{personId}` | None | 204, empty body |

IDs are GUIDs. Request/response JSON uses camelCase. Mutations require JSON
(`application/json` or a supported JSON media type). All request fields listed
above are required. Blank/whitespace names are rejected; accepted names are
trimmed by the domain. Participation uses a nullable boolean plus `[Required]`
on request DTOs, so omission/null is invalid while explicit `false` is accepted.
Shared domain text validation also rejects embedded null characters, which
PostgreSQL cannot store, before any write is attempted.

Creating a household atomically creates its first person as a financial
participant. Adding a person returns the exact created ID even when names repeat.
Person PUT replaces both editable fields atomically; there is no partial PATCH.
Household PUT replaces its editable name, not its roster. PUT never creates a
missing resource. Person removal is guarded by existing allocations, holdings,
financed-asset links, snapshot history and household minimum membership/participation.
A second DELETE for an already removed person returns 404; the resulting state
is unchanged. GET requests never change financial state.

`HouseholdResponse` contains `id`, `name`, and sorted `people`. `PersonResponse`
contains `id`, `householdId`, `displayName`, and `participatesInHouseholdFinances`.
Responses contain no EF navigation graphs or client-editable identity fields.
Reads of an individual person are scoped through the household roster.
There is no household listing/deletion or separate people-list endpoint in this
slice; the household GET includes its complete roster.

Example creation, after starting the updated API:

```sh
curl -i http://127.0.0.1:5190/api/households \
  -H 'Content-Type: application/json' \
  -d '{"name":"Example household","firstPersonDisplayName":"Alex"}'
```

Use the returned Location/IDs in subsequent requests. There is no login or
"currently selected household" on the server. Different clients can operate on
the same URLs independently of cookies. POST is not automatically deduplicated:
clients should not blindly retry creation after an ambiguous connection failure.

## Validation and errors

`[ApiController]` and DataAnnotations validate request DTOs before service calls.
Malformed JSON, missing/null fields and type mismatches return 400. GUID route
constraints produce 404 for non-GUID route segments. Unsupported methods produce
405, and unsupported body media types produce 415.

Errors use `application/problem+json`, with status/title and a trace ID. Validation
responses also carry an `errors` dictionary; clients should map its field keys
rather than parsing framework-generated prose. DataAnnotations field keys currently
use DTO property names such as `Name` and `ParticipatesInHouseholdFinances`.
Expected missing resources use `ResourceNotFoundException` and return 404.
Invalid supplied domain text uses `InvalidValueException` and returns 400.
`BusinessRuleViolationException` returns 409 with the domain explanation.
The household rules now use this specific subtype of InvalidOperationException;
the existing database tests assert that precise type. Other domains retain their
existing exception types until their endpoint slices establish the appropriate
translation. No blanket InvalidOperationException-to-conflict mapping is used.
Plain `ArgumentException` and `KeyNotFoundException` are also treated as unexpected
failures, so internal programming errors cannot masquerade as client mistakes.

Unexpected failures return a generic 500. Exception messages, database details
and stack traces are not included in public responses; diagnostics stay in server
logs. The exception handler also supports clients whose Accept header does not
request JSON. Status-code middleware supplies problem responses for otherwise
bodyless errors when a supported problem writer is available. Health endpoints
retain their readiness/liveness behaviour.

No successful-response cache or session state is maintained. These setup writes
use the existing serializable transaction/retry helper. There is no HTTP version
precondition for household/person edits yet: ordinary concurrent name edits can
overwrite one another. Snapshot expected-revision handling remains a separate,
existing contract to expose in its later API slice.

## Local access boundary

The existing Compose ports remain bound to `127.0.0.1`. No authentication middleware,
session cookies or permissive CORS policy is added. Every caller that can reach
these endpoints can use them: household IDs scope operations but are not access
permissions. The household check prevents assigning operations to the wrong
resource hierarchy; it is not authorization. People remain financial participants,
not login accounts. Authentication, household permissions and HTTPS must be planned
before exposing this deployment beyond the trusted local environment.

## Tests and build changes

`Hearthcast.Api.Tests` uses WebApplicationFactory/TestServer to send HTTP requests
through the application's real routing, binding, validation, controller, service
and EF pipeline. PostgreSQL is real; no HTTP port or live application process is
used. The existing disposable-database fixture is shared via an explicit source
link, without referencing the database-test assembly or duplicating its lifecycle.
The factory configures the isolated connection and replaces the context factory
with the fixture. It does not migrate or seed the application database.

The test host runs in Production mode to verify public errors. A test-only
controller deliberately throws an unexpected exception; it is registered only
by the test factory and is not part of the API assembly or runtime image.
Request tests cover lifecycle/Locations, independent clients without cookies,
duplicate labels, invalid JSON and fields, explicit-false participation, conflicts
and rollback, cross-household resource paths, missing resources, financial/history
guards, status codes/media types and safe 500 responses with differing Accept headers.
Regressions also cover null characters on all text mutation routes, required-field
error keys, and generic argument/lookup exceptions remaining private server errors.

The Docker test target now builds/runs both test projects through `Hearthcast.slnx`:

```sh
docker compose --profile test run --build --rm tests
```

The Docker context allowlist adds only the solution file. EF Core Relational is
explicitly pinned to 10.0.13 in the data project, matching the existing API design
package rather than resolving different EF patches in different projects. The
Npgsql provider version is unchanged. Both suites exercise the aligned runtime.

The running API has not been rebuilt or deployed by this slice. To use the new
endpoints after review, the standard Compose startup rebuilds the API and applies
pending migrations via the separate migration service. Normal API startup still
does not migrate or seed a database. Changes remain local and uncommitted.

Verification: the host build and both Docker test-project builds have zero warnings
or errors. The combined run passes all **416 integration cases**: 385 database/
service cases and 31 HTTP cases, using disposable PostgreSQL databases.

Framework references: [controller API conventions](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0)
and [ASP.NET integration testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).
