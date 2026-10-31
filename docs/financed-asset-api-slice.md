# Financed-asset creation and inventory API

This slice exposes creation, household-wide listing and individual reads of financed
assets. It builds on the existing model without adding tables or migrations.

| Method | Route | Success |
| --- | --- | --- |
| POST | `/api/households/{householdId}/financed-assets` | 201 full response and Location to GET |
| GET | `/api/households/{householdId}/financed-assets` | 200 array |
| GET | `/api/households/{householdId}/financed-assets/{financedAssetId}` | 200 full response |

## Request and response

Required POST fields: `groupId`, `name`, `currencyCode`, `countryCode`,
`originalLoanAmount`, `hasWithdrawalFacility`, `includeInHousehold`, `owners`,
`loanHolderPersonIds`, and `householdAllocations`. Optional fields are
`purchaseDate` (ISO date) and `purchasePrice`; missing/null means unknown.
The original loan amount and purchase price must be nonnegative. Zero is allowed.
Names are trimmed; currency/country codes are normalized to uppercase and must
have three/two ASCII letters respectively. Codes are checked for shape, not
looked up against an ISO catalogue. The group must belong to an Asset category.

Ownership and household allocation entries contain `personId` and
`shareBasisPoints` (10,000 = 100%). Each person must be unique within each list,
with positive shares of at most 10,000. Ownership must always total 10,000.
Included household allocations must independently total 10,000; excluded assets
must have an empty household allocation list. These arrays are explicitly required:
there is no default household split inferred from ownership.

`loanHolderPersonIds` contains distinct household person IDs, without percentages.
The list is required but may be empty, following the existing domain contract.
A person may be both an owner and a loan holder and have a different household
allocation. Ownership and loan holders may include nonparticipants; recipients
of household allocations must participate in household finances.

The response includes ID, household/group IDs, name, currency/country, purchase
metadata, original loan amount, facility/inclusion/retirement flags, and all three
associations. Association arrays are sorted by person ID. No navigation graphs
are serialized. Current valuation, outstanding balance and available-to-withdraw
amounts are snapshot data and cannot be submitted as setup fields.

The setup inventory includes excluded and retired assets, sorted by name then ID.
An existing household with no assets returns `[]`; missing households return 404.
All referenced people and groups must belong to the route household. IDs belonging
to another household return 404. This is resource scoping, not authorization.

## Validation and persistence

Missing/null required fields, invalid values, duplicates, wrong totals, extra JSON
members and a Debt-category group return 400. Ineligible household recipients return
409. Error responses use the existing problem JSON format with trace IDs. Request
validation errors name their fields. Known missing resources use explicit
`ResourceNotFoundException`; a Debt-category group uses `InvalidValueException`,
and participant eligibility uses `BusinessRuleViolationException`. Plain internal
argument/lookup exceptions retain the shared handler's generic 500 treatment.

Creation uses the existing serializable transaction/retry helper. Failed requests
leave no asset or association rows behind. Reads load the associations in a single
SQL statement to keep them in one database view, with identity resolution for lists.
No authentication, seeding or migrations on normal application startup are added.
There are no financed-asset update/delete endpoints in this slice. Association and
retirement changes are a follow-up; existing service methods remain available.

## Verification

HTTP tests run through the real routing, validation, service and EF pipeline against
disposable PostgreSQL databases. They cover independent-client persistence, distinct
association roles, normalized codes, purchase metadata, each required field in an
otherwise valid request, invalid shares, duplicate people, null collections/elements,
negative amounts, unknown setup fields, Asset/Debt grouping, foreign references,
nonparticipant ownership/borrowing versus household eligibility, excluded assets,
missing parents, and unchanged database state after rejected requests.

```sh
docker compose --profile test run --build --rm tests
```

The full suite previously passed **523 integration tests** (385 database/service
and 138 HTTP). After the review changes, the focused financed-asset HTTP suite
passes **44 cases**. Both test-project builds completed with zero warnings and
errors.

The running API has not been upgraded by this slice. Changes remain uncommitted
for review.
