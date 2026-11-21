# Spending setup — domain and persistence

17 November 2026. First backend slice of the
[spending and budget plan](spending-and-budget-schema-plan.md).

## Implemented boundary

`Hearthcast.Data/Spending` contains ExpenseCategory, Merchant, SpendingItem and
SpendingItemMerchantPrice. DbContext sets, EF configurations and the incremental
AddSpendingSetup migration persist them. The subsequent
[service layer](spending-setup-service-slice.md) is now implemented separately.
The incremental HardenSpendingSetup review migration adds identity length limits,
shared whitespace constraints and a stable usual-price index name.
The [setup API](spending-setup-api-slice.md) is also implemented. There is no UI yet.
There are no spending records, receipt-source links or
budget tables in this slice. The migration is generated and exercised in isolated
test databases; it has not been applied to the running application database.

Constructors are internal, as in existing domain entities; setup services load and
scope their inputs before construction. The data test
assembly has internal access to test this layer independently of service/API
implementations.

## Rules

- Categories and merchants are user-defined household setup, independent of the
  existing asset/debt classification. Names need not be unique. Rename and
  retirement preserve identity and relationships. Retirement is reversible and
  does not delete prices or cascade to other setup.
- Items have required category, code, name, UnitKey and UnitLabel, plus optional
  notes. Category changes must stay in the household. Empty notes become null.
- Codes are trimmed and stored uppercase; UnitKeys are trimmed and stored
  lowercase. Codes allow at most 64 characters; UnitKeys at most 32, enforced by
  domain validation and varchar lengths in PostgreSQL. Both use ASCII letters/digits with dots, underscores and hyphens,
  starting with a letter/digit. This gives consistent domain/database comparison
  without locale-dependent case folding. Item codes are unique within a household,
  including retired items; another household may reuse them. Display names/labels
  remain free text.
- Descriptive text uses an explicit shared trimming set: U+0009–000D, U+0020,
  U+0085, U+00A0, U+1680, U+2000–200A, U+2028–2029, U+202F, U+205F and U+3000.
  Domain methods strip surrounding characters from this set; PostgreSQL checks
  require nonempty text already trimmed with the same set. Empty notes become
  null. Internal whitespace is preserved; other characters such as U+200B are not
  silently treated as whitespace. Tests verify every listed character at both
  boundaries and compare the explicit policy with this target runtime's whitespace
  set, rather than assuming universal Unicode equivalence.
- Code can be corrected through the item-details service, which also refreshes
  linked receipt codes/names; UUID links and household uniqueness remain intact.
  UnitKey has no mutation method. Changing a pack size/meaning requires
  a new item. UnitLabel may be renamed for display. Usual prices retain their
  copied unit label; changing an item's display label does not mutate a price.
- Remembered prices are unique per household, item, merchant, currency and unit.
  A composite item/unit FK prevents storing a price against a different unit or
  changing a referenced item's unit identity. Item/category and price/merchant
  references also enforce household scope in PostgreSQL.
- Currency codes are explicitly supplied, trimmed uppercase ASCII three-letter
  codes, following existing backend code validation. This slice validates their
  shape, not exchange-rate availability or receipt minor units. Receipt currency
  support/rounding policy belongs to the later spending-capture slice. There is
  no hard-coded GBP default or conversion.
- Prices use unconstrained PostgreSQL numeric and C# decimal, with checks for
  nonnegative values below 1,000,000,000 and at most six fractional digits. Zero
  is allowed. Excess precision, NaN and infinities are rejected rather than rounded.
- SetUsualUnitPrice is an explicit operation. Successful calls update the UTC
  timestamp and GUID revision, including explicit confirmation of the same price.
  Invalid input leaves the entity unchanged. Revision is an EF concurrency token;
  stale persisted updates fail. Expected-revision service contracts are described
  in the service-layer note; HTTP contracts follow in their own slice.
- Referenced households, categories, merchants and items cannot be physically
  deleted through cascading relationships. Use retirement for setup lifecycle.
  Deliberate inclusion of retired setup for historical capture remains possible;
  services support explicit retired references and filtered active-item selection.

## Verification scope

Domain tests exercise normalisation, missing/invalid text and codes, currency
shape, price bounds/precision, nonmutating failures, household checks and identity
preservation. PostgreSQL tests exercise real migrations, price uniqueness and
decimal aggregation, household and unit FKs, reserved retired codes, stale edits,
deletion restrictions and direct-SQL constraint bypass attempts. Migration coverage
rolls back only this slice, reapplies it and verifies an existing financial snapshot's
amount, metadata and revision survive. Existing suite coverage also verifies a
complete migration-chain rollback/reapplication and model/snapshot agreement.

At completion of this slice, all 955 backend tests passed (621 data/domain and 334 API), including
56 new spending setup cases. The solution test run builds all backend projects;
the EF model matches its migration snapshot. Cached restore metadata emits NU1900
because the NuGet vulnerability feed is unavailable; compilation and tests pass
without new package dependencies.

Review hardening adds maximum/over-limit and long varied ASCII cases, service
rollback checks for overlength input, direct-SQL length checks, and surrounding
whitespace checks for every descriptive field. The incremental migration verifies
valid setup/price preservation and refuses to shorten existing overlength keys;
those require deliberate correction before migration. Neither spending migration
has been applied to the running application database.

After review hardening, all 1,000 backend tests pass (666 data/domain, 334 API),
including 101 spending setup tests. There are no failed or skipped cases. These
cover migration rejection without truncation as well as successful upgrade,
rollback/reapplication and model consistency. The only build warning remains the
unavailable NuGet vulnerability metadata feed (NU1900).

The [setup services](spending-setup-service-slice.md) and
[API](spending-setup-api-slice.md) are implemented, as is
[record domain/persistence](spending-records-slice.md) and
[record services](spending-record-service-slice.md), followed by
[record HTTP capture](spending-record-api-slice.md). Budget domain/persistence is next;
UI remains deferred until backend workflows are established.
