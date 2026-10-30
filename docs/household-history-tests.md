# Household history and snapshot behaviour specification

Agreed 30 October 2026. All names and amounts are synthetic. Each test owns an
isolated PostgreSQL database; this data never seeds the application database.

## Reference household

Cedar household contains Adult A and Adult B (participants), and Child C
(nonparticipant). People without items are valid and still appear in the roster.
A separate household with matching display names exercises identity boundaries.

January 2023 reference capture, Month precision:

| Item | Category / group | Holder | Currency | Value | Household allocation |
| --- | --- | --- | --- | ---: | --- |
| Retirement fund A | Investments / Retirement | A | ZAR | 100,000 | A 100% |
| Pension B | Investments / Pension | B | ZAR | 80,000 | B 100% |
| ETF Beta | Investments / TFSA | A | ZAR | 25,000 | A 100% |
| ISA A | Investments / ISA | A | GBP | 1,000 | A 100% |
| Emergency savings | Savings / Emergency | B | GBP | 2,000 | A 50%, B 50% |
| Deposit savings | Savings / Deposit | B | GBP | 0 | B 100% |
| Child savings | Savings / General | C | GBP | 300 | Excluded |
| Personal reserve | Savings / General | A | GBP | 400 | Excluded |
| Unsecured loan | Borrowing / Loans | A | GBP | 1,500 | A 100% |

Investments and Savings have financial type Asset; Borrowing has Debt. Names do
not trigger system behaviour. Reference reporting expectations for the future
summary slice: ZAR 205,000 (A 125,000; B 80,000), GBP net 1,500 (A 500; B 1,000).
These expectations do not add reporting services to the snapshot slice.

## Chronological story

- January 2015: SA-only retirement 10,000, pension 8,000 and ETF Alpha 2,000.
  Child C has no items. Month precision.
- March 2015: skip February entirely. Retirement confirmed as 11,000; unavailable
  pension carries 8,000 with January confirmation. No synthetic February capture.
- April 2015: explicitly confirm unchanged retirement 11,000. Confirmation advances.
- February 2020: introduce the GBP items with the values in the reference table.
- March/April 2021: ETF Alpha gets a final confirmed zero, is retired, and ETF Beta
  is created with its own identity and a confirmed value of 25,000.
- January 2023: record all reference values, then a dated January 18 capture using
  the month-only capture as an explicitly selected source.
- June 2023: emergency allocation changes to 70/30; B remains actual holder.
  Rename live household/person/category/group/item labels. Old captures retain
  old labels and allocations. Select January 18 explicitly because the preceding
  January captures overlap in precision.
- 2024: create a later capture, then correct January 2023 ISA from 1,000 to 900.
  Existing later copies stay 1,000, with their saved confirmation metadata.
- 2030: long gap; Child C becomes a participant but their item remains excluded.
  A subsequent explicit inclusion change makes it count. No automatic inclusion.

Setup evolves between captures. Backdating current setup does not reconstruct a
historical inventory; historical templates/import are separate future work.

## Capture and correction contract

- CreateAsync remains an explicit fresh capture: omitted inputs are missing.
- CreateNextAsync starts from one prior snapshot. Supplied inputs confirm or clear
  a value; omitted inputs copy that source entry. New items remain missing.
- Automatic selection uses financial date/precision, not creation time or IDs.
  Definitely future captures are ignored. An overlapping target date or overlapping
  most-recent source candidates requires an explicit source ID. Explicit selection
  acknowledges ordering within an uncertain/same-day period; a definitely future
  source is always invalid. A later unambiguous source supersedes older ambiguity.
- Day precision describes one day; Month describes the full calendar month for
  ordering, while retaining first-of-month storage. No invented exact day.
- Copy only the selected snapshot: a missing value stays missing, with no search
  for an older known balance. Explicit selection of an older source is deliberate.
- Carry known values, including zero, with CarriedForward origin, the immediate
  source entry ID and its last-confirmed date/precision. Several generations keep
  the original confirmation date. Unknown confirmation metadata remains unknown.
- Current active setup supplies the new roster, holder, labels, classification,
  inclusion and shares. Retired items are absent; replacements have new IDs.
- Currency must match for a carry. Supply a new value or clear it explicitly when
  currency changes. Country alone does not determine currency.
- CorrectValuesAsync updates only supplied existing saved entries. It can correct
  retired items and uses the saved snapshot date as confirmation date. Explicit
  unchanged values reconfirm; null clears. Copied reporting settings remain intact.
- Corrections require the revision returned with the saved snapshot. A stale
  revision is rejected; a successful correction changes the revision and edit time.
  This token protects writes; it is not an item-version/history model.
- Corrections do not modify any later amounts, origins or provenance references.
- Bulk captures/corrections commit atomically. Concurrent reads see one coherent
  snapshot, including its header revision and item values.

## Boundary catalogue

Retain existing tests for household minimums, participation, valid names/codes,
Asset/Debt, holder/allocation independence, fractional shares, totals, eligibility,
foreign-household and foreign-snapshot keys, uniqueness, deletion restrictions,
exact/native numeric values, invalid amounts, rollback and serializable retries.

Add carry/correction cases for missing/zero, unchanged confirmation, selected-source
isolation, skipped dates, month/day overlaps, same-day ties, backdated creation,
new/retired/replacement items, currency/country changes, confirmation provenance,
invalid/foreign sources, stale/concurrent edits and failed multi-entry updates.
The database enforces same-item/currency provenance and value-state consistency;
source chronology is a transactional service rule.

Financed assets remain a later slice. Its fixture will use purchase price ZAR
1,100,000, original loan 1,000,000, valuation 1,400,000, outstanding 600,000 and
AvailableToWithdraw 250,000. Owners and household allocation are A/B 50/50, with
B as loan holder. Expected equity 800,000; loan reduction 400,000; reduction
excluding withdrawable funds 150,000. Variants include negative equity, paid-off
borrowing, missing independent balances and no withdrawal facility.
