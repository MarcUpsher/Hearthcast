# Categories and groups REST API

Classification is household setup: categories define Asset or Debt, and groups
organize items under a category. This slice exposes creation, reads, lists and
renaming through the existing stateless API. It adds no schema migration.

## Routes

Let `C` mean `/api/households/{householdId}/categories` and `G` mean
`C/{categoryId}/groups`. All IDs are GUIDs.

| Method | Route | Body | Success |
| --- | --- | --- | --- |
| POST | C | `name`, `financialType` | 201 CategoryResponse, Location points to GET |
| GET | C | None | 200 CategoryResponse array |
| GET | C/{categoryId} | None | 200 CategoryResponse |
| PUT | C/{categoryId} | `name` | 200 renamed CategoryResponse |
| POST | G | `name` | 201 GroupResponse, Location points to GET |
| GET | G | None | 200 GroupResponse array |
| GET | G/{groupId} | None | 200 GroupResponse |
| PUT | G/{groupId} | `name` | 200 renamed GroupResponse |

Example category creation:

```json
{"name":"Investments","financialType":"Asset"}
```

`financialType` uses the string enum values `Asset` and `Debt` in responses.
Input enum names are case insensitive. Numbers, numeric strings, unknown values,
missing values and null are rejected. The request contract also validates enum
membership, including combined names that resolve to an undefined value.
The converter is scoped to classification DTO properties; other API JSON contracts
are unaffected. The domain independently rejects undefined financial types with
`InvalidValueException`.

Names are required, trimmed, and cannot be blank or contain null characters.
Rename requests contain only `name`: Asset/Debt type and parent associations
cannot be changed by these endpoints. Classification request DTOs reject unknown
JSON members rather than silently ignoring attempted type or relationship changes.
A PUT to a missing resource returns 404, without creating a new record.

CategoryResponse contains `id`, `householdId`, `name`, `financialType`.
GroupResponse contains `id`, `householdId`, `categoryId`, `name`.
No EF navigation graphs are serialized. Lists use database name ordering with ID
as a stable tie breaker; duplicate names are allowed and retain distinct IDs.
Existing parents with no children return `[]`; missing parents return 404.
No pagination is introduced for these small setup lists.

Every category query includes household ID. Every group query includes household,
category and group IDs as applicable. A real ID under the wrong parent path
returns 404. Household scoping is resource integrity, not authorization; the local
access boundary described in the household API guide still applies.

## Behaviour and validation

Controllers handle routes and DTO mapping, while ClassificationService owns
queries and mutations. Renames save only the entity name, keeping identity, type,
item references and parent relationships unchanged. Existing snapshots retain
the category/group labels copied when they were captured. Future snapshots use
current labels. There is no deletion or movement between parents in this slice.

The shared error handler maps explicit resource-not-found and invalid-value
exceptions to 404 and 400. Invalid JSON and DTO validation return 400; field
validation includes an `errors` dictionary with DTO property names. Error bodies
use problem JSON and carry a trace ID. Unsupported DELETE requests return 405.
As with household setup, ordinary name edits have no HTTP revision precondition;
concurrent renames can overwrite one another.

## Verification

HTTP integration tests run through the real controller/service/EF pipeline against
disposable PostgreSQL databases. They cover both financial types, Location URLs,
independent-client reads, empty lists, sorting and duplicate labels, invalid enum
representations, required-field errors, invalid names and unchanged persisted
state, rejected type/parent edits, missing resources, household/category scoping,
and renames preserving current references and earlier snapshot labels while new
snapshots use the renamed labels. Lower and upper case enum requests are also
verified against canonical response names.

The database tests continue to verify classification constraints and now assert
the explicit domain exception types used by these endpoints.

```sh
docker compose --profile test run --build --rm tests
```

Verification passes all **445 integration cases**: 385 database/service cases and
60 HTTP cases, including 29 new classification cases. Both Docker test-project
builds complete with zero warnings and errors.

The running application has not been rebuilt or deployed by this slice. These
classification changes remain uncommitted for review.
