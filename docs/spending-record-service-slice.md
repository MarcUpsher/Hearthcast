# Spending-record services

17 November 2026. Builds on [record domain/persistence](spending-records-slice.md).
SpendingRecordService implements scoped capture, history, correction, voiding,
last-paid lookup and explicit usual-price adoption. No schema, endpoints, UI,
budget workflow or live deployment is added. The subsequent
[record API slice](spending-record-api-slice.md) now supplies registration, HTTP
mapping and bounded history paging.

## Commands and reads

Public service values are distinct from HTTP contracts. SpendingRecordValues
contains merchant/date/currency, ordered SpendingLineValues, kind, optional original
payment, receipt total, reference and notes. Lines contain setup IDs plus actual
amounts and optional product/receipt detail. Callers never supply entity instances
or create hashes. RememberSpendingPrice chooses a one-based line position, an
explicit amount and an optional expected usual-price revision.

| Operation | Behaviour |
| --- | --- |
| CreateAsync | Takes household, client UUID and values; returns SpendingRecordCaptureResult with Record and WasCreated. Optional price choices save in the same transaction. |
| GetAsync | Returns one full aggregate, including voided history. |
| GetOriginalPaymentVoidStatesAsync | Resolves distinct original-payment IDs in one scoped projection for API labels, without loading original lines. |
| ListAsync | Full aggregates, inclusive optional from/to dates; excludes voided records by default, includeVoided=true retains them. |
| ListPageAsync | Bounded full-receipt pages with context-bound keyset cursors; the API uses this instead of ListAsync. |
| ListSupportedCurrenciesAsync | Requires a household and returns the supported capture currency policy for discovery. |
| CorrectHeaderAsync | Revision-checked date, receipt total, reference and notes; preserves copied context. |
| CorrectLineAsync | Revision-checked amounts, quantities/prices, promotion and notes; preserves copied context and optionally adopts chosen prices. |
| ReplaceAsync | Revision-checked merchant/header and explicit replacement lines; removes old rows, detaches sources and optionally readopts prices. |
| VoidAsync | Revision-checked exclusion from spending without deleting header, lines, create identity or linked refunds. |
| FindLastPaidAsync | Exact item/merchant/currency/unit payment history at or before an explicit date, with optional excluded record. |

Reads/edits load complete aggregates with lines ordered by Position. History sorts
by spending date, creation time and UUID descending. Empty inventories are valid;
missing households/records and foreign IDs are ResourceNotFoundException. Date
ranges cannot be inverted. Item/category/merchant/original-payment references are
resolved inside the selected household; retired setup remains explicitly usable.
Void records cannot be edited. Currency/precision, kind and original-payment
association remain fixed as documented in the domain slice.

## Create idempotency

Before the first await, the service freezes caller-owned collections and computes
the server SHA-256 hash from a version-one canonical representation of the request.
It includes household/client UUID, explicit header values, ordered lines and price
choices. Optional text is normalized, currency case is normalized, decimal values
use invariant G29 formatting, and price choices sort by line position. Line order
is meaningful; equivalent decimal scales, trimmed text and absent/empty price
choice collections are not different requests. Expected price revisions are part
of the requested adoption intent. The version-one format is a persisted contract;
future changes must preserve existing retry compatibility deliberately.

The hash never includes generated line IDs, timestamps, current setup names,
retirement state, usual prices or current currency precision. A matching household/
UUID/hash retry returns the existing record's current state with WasCreated=false,
even after correction or voiding. It does not reload current setup or replay price
updates. A changed create payload conflicts; use a revision-checked edit instead.
Foreign-household UUID collisions return a generic conflict without record data.

Concurrent primary-key creates retry the entire transaction against the winning
record. Identical requests resolve to one creation and one replay; conflicting
requests leave one record and a BusinessRuleViolationException. Receipt references,
dates and merchants remain nonunique and are not idempotency keys. No record
deletion is exposed, so retained UUIDs/hashes continue to protect delayed retries.

## Usual prices and source cleanup

Capture never copies actual prices into usual prices implicitly. Price choices
must refer to itemised, nonvoid Payment lines. Choose at most one source per item/
merchant/currency/unit, even when a product occurs on multiple lines. The chosen
amount is explicit: callers can distinguish nominal from effective discounted price.

A null expected price revision means create only. If a usual price already exists,
the caller must supply its current revision; a missing/stale revision conflicts.
If a supplied revision's price no longer exists, that also conflicts. Explicit
adoption creates/updates the price and sets its source line atomically with the
receipt. Refunds, quick lines and amount-only product lines cannot supply prices.

Replacement clears sources to removed lines before deleting those rows. Voiding
clears sources to retained voided lines. Removing quantity/unit price clears that
line's sources. Amount-only corrections that retain eligibility do not propagate
into remembered amounts or invalidate compatible links.

Detachment retains the copied amount and advances the price revision. An explicit
readoption in the same replacement compares the caller's token to the revision read
before this transaction's own detachment, not its newly generated cleanup token.
External concurrent changes still conflict or are preserved after a full retry;
cleanup never overwrites an unrelated new price/source. A manual price edit that
loses a revision race must reload before retrying.

## Transactions and errors

Every mutation uses SerializableTransaction with fresh-context bounded retries for
serialization/deadlock aborts. Source-detachment saves and later header/line/price
saves remain inside one outer transaction. Validation, cancellation or late save
failure rolls back all of them. Ambiguous network/commit failures are not blindly
retried; callers can retry creates safely with their original UUID and payload.

Record revisions are checked inside every attempt, so a retry cannot silently
adopt a newer token. EF concurrency failures map to SpendingRecordConflictException
or SpendingPriceConflictException according to the affected entity. Competing usual-
price creations also return price conflicts; conflicting client IDs return a generic
business conflict. Invalid values remain validation errors. HTTP status mapping
is documented in the [record API slice](spending-record-api-slice.md).

## Last-paid semantics

FindLastPaidAsync requires scoped item/merchant IDs and an explicit cutoff date.
It ignores refunds, voided records, amount-only/unlinked lines and later spending
dates, matching the item's stable unit identity and exact currency. An optional
excluded record must itself belong to the household. Missing matches return null;
there is no fallback or conversion. Read lookup validates currency shape rather
than today's capture list, allowing historical codes to remain queryable if policy
changes. New capture still validates the explicit supported-currency policy.

Selection is spending date, record creation time, record UUID, line Position and
line UUID descending. The result includes record/line IDs, saved unit/currency,
quantity, nominal unit price, net amount, effective unit price and promotion status.
For repeated products on one receipt the last positioned eligible line wins; the
service does not invent an average or assume a nonpromotional price. The result
is informational and never changes the remembered usual price.

## Verification and next work

The 33 new focused tests pass. They cover scoped capture/history/edits, mixed detail,
normalized retries after setup changes/corrections/voiding, competing creates,
explicit price adoption, stale-price rejection, source cleanup and readoption,
linked refunds, last-paid eligibility/ties, invalid replacements, late save failures,
cancellation and cleanup races with manual price edits.

Full backend verification passes: 1,151 tests (765 data/domain, 386 HTTP API), with
no failures or skipped tests. The run builds all backend projects and includes
existing migration/model checks. No dependencies were added; the existing NU1900
warning remains for unavailable NuGet vulnerability metadata. The running app and
database have not been deployed or migrated for this service-only slice.

## Review follow-up and record API acceptance criteria

The focused service review found no confirmed correctness defect. A fixed
version-one hash fixture now protects compatibility with previously persisted
requests, independently of runtime-to-runtime replay comparisons. It covers decimal
scale, optional/trimmed text, ordered lines, reordered price choices and two cultures.
Changing the expected hash to accommodate a refactor would break this contract.
Both compatibility cases pass. Only tests/documentation changed in this follow-up;
the prior full-suite result above was not rerun for it.

The [record API slice](spending-record-api-slice.md) implements the review's bounded
page/cursor contract and parent-before-lines query, equal-date continuation tests,
record 409 mapping, client UUID preservation, 201/200 replay distinction and nullable
required amount contracts. The unbounded ListAsync remains an internal inventory
convenience and is not wired to a general history route.

Next: budget domain/persistence. UI remains deferred.
