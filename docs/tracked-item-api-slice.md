# Tracked-item creation and inventory API

A tracked item is a named fund, savings pot or debt whose value can be recorded in a
snapshot. This API slice exposes its setup and inventory. It adds no migration.

## Routes and contract

| Method | Route | Result |
| --- | --- | --- |
| POST | `/api/households/{householdId}/tracked-items` | 201 with a tracked item and Location to its GET |
| GET | `/api/households/{householdId}/tracked-items` | 200 array, ordered by name and ID |
| GET | `/api/households/{householdId}/tracked-items/{trackedItemId}` | 200 tracked item |

The POST JSON requires `groupId`, `holderPersonId`, `name`, `currencyCode`,
`countryCode`, and `includeInHousehold`. An optional `allocations` array contains
`personId` and `shareBasisPoints` (10,000 means 100%). Omitting allocations assigns
100% to the holder for an included item, or no shares for an excluded item.
Explicit shares must be unique, each positive, and total 10,000 for an included
item. Explicit null allocations use the same default as omission; an explicit
empty array is accepted only for an excluded item. Excluded items cannot have shares. The holder remains the actual account
holder even if household shares belong partly or entirely to other participants.
A person outside household finances can hold an excluded item; only participating
people can receive household shares.

The response returns item ID, household ID, group ID, holder person ID, name,
currency and country codes, inclusion and retirement flags, and the saved shares.
Codes are normalized to uppercase. All items, including excluded or retired ones,
are part of the inventory response; retirement will be exposed in a later slice.
The request does not accept a starting amount: values belong to snapshots.

Existing households with no items return `[]`; missing households and cross-household
item/group/holder/allocation references return 404. Invalid fields or share totals
return 400. Allocating to a nonparticipant returns 409. Responses use the shared
problem JSON format on errors. There is no item update or delete endpoint here;
changing allocations and retirement status are the next setup slice.

## Verification

HTTP integration tests exercise real routing, validation, services and disposable
PostgreSQL databases. They cover default, explicit and excluded shares; persistence
across clients; names and codes; invalid, duplicate, cross-household and ineligible
shares; missing parents; and unchanged state after rejected creation. Required
fields are checked independently against otherwise valid requests, including
named validation errors. Allocation tests also cover null elements, missing share
properties, empty IDs, and omitted/null/empty array semantics. The allocation
entity owns participant eligibility validation for both creation and updates.

```sh
docker compose --profile test run --build --rm tests
```

Verification: **478 integration tests pass** (385 database/service and 93 HTTP)
with zero build warnings or errors. The HTTP suite includes 33 tracked-item
cases.

The running application has not been upgraded by this slice. Changes remain
uncommitted for review.
