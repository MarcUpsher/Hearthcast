# Ordinary snapshots: capture and reload

Implemented locally, 29 October 2026. This is the first review point within the
ordinary-snapshot slice: save and reload a complete dated capture. Carry-forward
and corrections follow separately, before financed assets or the bulk API.

## Files to review

| File | Responsibility |
| --- | --- |
| [Snapshot.cs](../backend/src/Hearthcast.Data/Snapshots/Snapshot.cs) | Shared header, date precision, copied household name, roster and item entries. |
| [SnapshotPerson.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotPerson.cs) | Original person identity with copied name and participation. |
| [ItemSnapshot.cs](../backend/src/Hearthcast.Data/Snapshots/ItemSnapshot.cs) | Full value, copied holder/classification/reporting context and confirmation metadata. |
| [ItemSnapshotHouseholdAllocation.cs](../backend/src/Hearthcast.Data/Snapshots/ItemSnapshotHouseholdAllocation.cs) | Copied share assigned to a person in this snapshot. |
| [SnapshotService.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotService.cs) | Transactional capture from current setup, and household-scoped reload. |
| [ItemSnapshotValue.cs](../backend/src/Hearthcast.Data/Snapshots/ItemSnapshotValue.cs) | Input value containing a tracked-item ID and nullable amount; not an entity. |
| [DecimalTextConverter.cs](../backend/src/Hearthcast.Data/Persistence/Conversions/DecimalTextConverter.cs) | Exact invariant decimal text storage. |
| [Configurations](../backend/src/Hearthcast.Data/Persistence/Configurations) | Four matching snapshot mappings, constraints and relationship keys. |
| [SnapshotPersistenceTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/SnapshotPersistenceTests.cs) | Capture/reload, metadata preservation, amounts, dates and migration upgrade. |
| [SnapshotIntegrityTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/SnapshotIntegrityTests.cs) | Atomicity, malformed setup, database boundaries, uniqueness and deletion restrictions. |

## Capture behaviour

`SnapshotService.CreateAsync` accepts a household ID, optional date, optional item
values and Day/Month precision. It defaults to today's local date and Day
precision. Month precision normalises the supplied date to the first of that
month; it does not assert that the valuation happened on that exact day. Entry
and update timestamps are separate UTC values, initially equal.

One serializable transaction reads the complete household roster and active
tracked items, builds the copied graph, and saves everything. It captures all
people, including nonparticipants and people without items. It captures included
and excluded active items. Retired items are omitted. A household with no items
can still have a snapshot.

Each active item gets exactly one ItemSnapshot. Values are optional:

- Omitted item or explicit null: null amount, Unrecorded origin and no confirmation
  date/precision. No previous balance is carried in this first implementation.
- Supplied nonnegative amount, including zero: Confirmed origin and the capture's
  financial date/precision as its last confirmation.
- Negative amounts, duplicate IDs, invalid precision, and IDs for foreign,
  unknown or retired items reject the whole capture.

Malformed current allocations are rejected rather than copied into inconsistent
history. Included entries require eligible recipients totalling 10,000 basis
points; excluded entries require no allocation rows.

The service returns the full saved graph. `GetAsync(householdId, snapshotId)`
reloads it from copied tables, without loading names or settings from live setup.
Its detached result includes the saved holder and allocation recipients.

## Saved history and database boundaries

The four tables are Snapshots, SnapshotPeople, ItemSnapshots and
ItemSnapshotHouseholdAllocations. They follow the keys in the relational plan.

Each saved item contains its original tracked-item/group/category IDs alongside
copied names, Asset/Debt type, currency, country and household inclusion. The
actual holder points to SnapshotPerson. Household allocations independently copy
their recipients and basis-point shares. Reports should use these copies.

Composite foreign keys enforce original identities within the household and
holders/recipients within the same snapshot. Unique indexes allow one original
person and one original item per snapshot. Required names, code formats, flags,
share ranges and amount/origin/confirmation consistency have row constraints.
Allocation totals and eligibility remain transactional application rules.

All deletion relationships restrict; there is no cascading removal of history.
HouseholdService also gives a domain error when removing a person who appears in
snapshot history, even if they currently hold no items. Names and participation
can still change without changing saved copies.

Multiple captures in the same month or on the same date are permitted. The
snapshot ID identifies the capture; monthly reporting selection remains a later
decision.

## Exact money

Amounts use .NET decimal and SQLite TEXT, with invariant `G29` formatting. The
converter parses exponent notation too, preserving the smallest decimal values.
It applies to the nullable amount property; null remains SQL NULL. EF does not
pass nulls through value converters, as documented in Microsoft's
[value conversion guidance](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions).

Round-trip tests run under a comma-decimal culture, covering zero, fractional
precision, decimal.MaxValue and 0.0000000000000000000000000001. They check the
actual SQLite storage type and string as well as the reloaded decimal.

There is no fixed currency scale or implicit rounding. Future aggregation starts
with materialised .NET decimal values, grouped by currency. Do not sort or sum
these custom TEXT values as though they were SQL numeric columns. Nonnegativity
and valid numeric format are application rules; direct SQL can bypass those
checks. A floating-point database check would not establish exactness.

## Verification and scope

The fourth migration, CreateOrdinarySnapshots, adds only the four tables and
their indexes/constraints. It preserves existing setup and allocations, does not
seed financial data and does not edit previous migrations. Upgrade and full-chain
rollback/reapply tests pass; the EF model matches its migration snapshot.

Build: zero warnings/errors. SQLite integration suite: 136 passing cases, including
42 new snapshot cases. A synthetic failure on the last association insert proves
that the header, roster and item entries roll back together.

Backdating currently copies **current setup**. It does not reconstruct historical
items or allocations. Historical templates/import metadata, carry-forward,
corrections, financed assets, summaries and API/UI workflows are subsequent work.
There is no persistent application database migration on API startup. This local
slice is uncommitted and unpushed for review.
