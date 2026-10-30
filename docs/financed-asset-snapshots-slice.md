# Financed-asset snapshots

Implemented locally, 30 October 2026, after the pushed financed-asset setup
checkpoint. Ordinary and financed entries now belong to one Snapshot, with one
copied roster, revision and correction transaction.

## Files to review

| File | Responsibility |
| --- | --- |
| [FinancedAssetSnapshot.cs](../backend/src/Hearthcast.Data/Snapshots/FinancedAssetSnapshot.cs) | Copied setup, three independent balance states, associations and derived amounts. |
| [FinancedAssetBalanceKind.cs](../backend/src/Hearthcast.Data/Snapshots/FinancedAssetBalanceKind.cs) | The three supported financial inputs. |
| [FinancedAssetSnapshotValue.cs](../backend/src/Hearthcast.Data/Snapshots/FinancedAssetSnapshotValue.cs) | One explicit balance input; null clears it and omission allows carry. |
| [FinancedAssetSnapshotOwner.cs](../backend/src/Hearthcast.Data/Snapshots/FinancedAssetSnapshotOwner.cs) | Saved owner shares linked to copied people. |
| [FinancedAssetSnapshotLoanHolder.cs](../backend/src/Hearthcast.Data/Snapshots/FinancedAssetSnapshotLoanHolder.cs) | Saved named loan holders. |
| [FinancedAssetSnapshotHouseholdAllocation.cs](../backend/src/Hearthcast.Data/Snapshots/FinancedAssetSnapshotHouseholdAllocation.cs) | Saved household shares and participant eligibility. |
| [Snapshot.cs](../backend/src/Hearthcast.Data/Snapshots/Snapshot.cs) | Captures both entry types and validates all corrections before changing values. |
| [SnapshotService.cs](../backend/src/Hearthcast.Data/Snapshots/SnapshotService.cs) | Current setup, source selection, whole-capture correction and consistent reload. |
| [FinancedAssetSnapshotTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/FinancedAssetSnapshotTests.cs) | Lifecycle, independent balances, copied context, calculations and invalid input. |
| [FinancedAssetSnapshotIntegrityTests.cs](../backend/tests/Hearthcast.Data.Tests/Snapshots/FinancedAssetSnapshotIntegrityTests.cs) | PostgreSQL constraints, migrations, malformed setup, rollback and concurrent edits. |

Each new entity has its own EF mapping. AddFinancedAssetSnapshots creates the
entry and three copied association tables. Prior migrations remain unchanged.

## Capture and correction

CreateAsync and CreateNextAsync accept an optional financedAssetValues collection.
Each input specifies the original FinancedAssetId, a FinancedAssetBalanceKind and
an Amount. Values are unique by asset and balance. Ordinary values retain their
existing meanings. CancellationToken remains the final parameter; positional
callers that previously passed it after ordinary values must now name it or supply
the extra argument.

Fresh capture includes all active configured assets. Omitted balances are missing.
Next capture uses the same selected source snapshot for both entry types. For each
balance, a supplied amount confirms it at the new financial date; supplied null
clears it; an omitted value carries from the selected source entry. New identities
are missing, retired assets are omitted, and no older source is searched. Same
names never identify assets. Backdated captures still use current setup; this is
not an automatic reconstruction of the historic inventory.

CurrentValuation, OutstandingLoanBalance and AvailableToWithdraw each have their
own amount, origin, last confirmation date/precision and carry-source entry ID.
The source ID references a FinancedAssetSnapshot row, not the parent Snapshot.
Several carries retain the original confirmation and immediate source entry ID.
Zero is known; null is missing. Unknown confirmation metadata remains unknown.
Changing currency requires supplying or explicitly clearing each applicable balance.
Country changes do not convert amounts.

HasWithdrawalFacility is copied from setup. With no facility, AvailableToWithdraw
and its confirmation/source metadata stay empty, and a nonnull withdrawal input
is rejected. When a facility is removed from current setup, the next capture clears
that balance; older captures retain their copied facility and value. Corrections
always use the saved facility flag.

CorrectValuesAsync accepts ordinary values, financed values, or both. A correction
changes only existing saved entries, including retired assets, and confirms at the
saved financial date. It cannot add an asset to an older capture. All inputs are
validated before mutation, and both types commit atomically. One expected revision
protects the whole capture, including concurrent edits to different entry types.
Saved metadata, allocations, creation time and existing descendants remain intact.
Source links describe the copy operation; subsequent source corrections do not
rewrite descendants or guarantee that the source's current amount still matches.

Reads retain the existing RepeatableRead transaction for the split graph. Capture,
source reads and correction retain the serializable transaction/retry helper.

## Calculations and reporting

The following nullable decimal properties are derived from saved values rather
than stored as duplicate amounts:

| Property | Calculation |
| --- | --- |
| CurrentEquity | CurrentValuation - OutstandingLoanBalance |
| LoanBalanceReduction | OriginalLoanAmount - OutstandingLoanBalance |
| LoanBalanceReductionExcludingWithdrawableFunds | OriginalLoanAmount - OutstandingLoanBalance - AvailableToWithdraw, only with a withdrawal facility |

Missing inputs make only their dependent figures missing. Negative results remain
negative. AvailableToWithdraw is informational because overpayments already reduce
outstanding debt; it is not added to equity again. Loan balance reduction does not
represent cumulative payments or interest paid.

Recorded balances retain the full nonnegative decimal range. If the reduction
excluding withdrawable funds falls outside that range, reading the property throws
an explicit OverflowException. It is not clamped or returned as null, which is
reserved for missing inputs or a withdrawal facility that does not apply. The
recorded balances remain available for inspection and correction.

Synthetic example: valuation 1,400,000, original borrowing 1,000,000, outstanding
600,000 and withdrawable 250,000 yield equity 800,000, reduction 400,000 and
reduction excluding withdrawable funds 150,000. Household reporting will count
included equity once and use copied household shares for its person breakdown.
Actual ownership and named loan holders remain separate copied associations.

## Constraints and review limits

Composite keys enforce same-household original identities and same-snapshot people.
Each source FK enforces the same original asset and currency, and self-sources are
rejected. Each balance has its own native finite/nonnegative numeric and value-state
checks. No-facility withdrawal balances must be missing. Ownership/household totals,
participant eligibility, chronology and current Asset classification are also
validated while creating the saved graph. These collection rules remain application
rules; direct SQL is not a supported capture workflow.

Downgrading this additive migration removes financed snapshot tables and their
rows; use backups for real data. Migration tests use disposable databases and
explicit migration boundaries. When testing an older schema, they query only the
tables available at that boundary rather than calling services from the newer app.

Verification: the build has zero warnings/errors. All 330 PostgreSQL integration
cases pass in one full Docker run, including 57 new financed-snapshot cases. The
controlled split-read test now verifies both ordinary and financed values during
a concurrent mixed correction. Review coverage also exercises decimal overflow and
the valid boundary, and reloads all three balances to verify confirmation metadata
after replacing a carry with zero. Tests create and remove isolated databases; the
application database remains at its original migration with no household/snapshot
data, and no disposable databases remain.

The running application database/API have not been upgraded. Only the test image
is rebuilt. No real financial data, summaries, currency conversion, finance HTTP
endpoints or frontend are added in this slice. Changes remain local for review.
