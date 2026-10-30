# Ordinary snapshots: capture and reload

Implemented locally, 29 October 2026. This is the first review point within the
ordinary-snapshot slice: save and reload a complete dated capture. Carry-forward
and corrections are now implemented in the [second review point](snapshot-lifecycle-slice.md),
before financed assets or the bulk API.

## Files to review

| File | Responsibility |
| --- | --- |
| [Snapshot.cs](../backend/src/Hearthcast.Data/Snapshots/Snapshot.cs) | Shared header, date precision, copied household name, roster and item entries. |
| [SnapshotPerson.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotPerson.cs) | Original person identity with copied name and participation. |
| [ItemSnapshot.cs](../backend/src/Hearthcast.Data/Snapshots/ItemSnapshot.cs) | Full value, copied holder/classification/reporting context and confirmation metadata. |
| [ItemSnapshotHouseholdAllocation.cs](../backend/src/Hearthcast.Data/Snapshots/ItemSnapshotHouseholdAllocation.cs) | Copied share assigned to a person in this snapshot. |
| [SnapshotService.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotService.cs) | Transactional capture from current setup, and household-scoped reload. |
| [ItemSnapshotValue.cs](../backend/src/Hearthcast.Data/Snapshots/ItemSnapshotValue.cs) | Input value containing a tracked-item ID and nullable amount; not an entity. |
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

Amounts now use .NET decimal and native PostgreSQL numeric, without a fixed scale
or text converter. Round-trip tests cover a comma-decimal culture, zero,
fractional precision, decimal.MaxValue and the smallest nonzero decimal value.
Database aggregation is exact; nonnegative/finite amount constraints also apply
to direct SQL writes. See [containers and PostgreSQL](containers-and-postgres.md).

## Verification and scope

The original snapshot checkpoint passed 136 SQLite cases and was committed at
`664b27e`. PostgreSQL now replaces that provider and its migrations. The current
integration suite retains capture/history/constraint/rollback coverage and adds
numeric and transaction-retry checks. The PostgreSQL baseline includes all four
snapshot tables alongside the existing setup tables.

Backdating currently copies **current setup**. It does not reconstruct historical
items or allocations. Historical templates/import metadata, financed assets,
summaries and finance API/UI workflows remain
subsequent work. Container startup applies migrations in a separate one-shot
service; ordinary API startup does not migrate or seed data.
