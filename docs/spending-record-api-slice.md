# Spending-record API and bounded history

17 November 2026. Exposes the tested [record services](spending-record-service-slice.md)
through household-scoped routes and explicit HTTP contracts. Includes a bounded
history service contract; no new schema, UI or budget features. This slice has not
been deployed to the running app/database.

## Routes and responses

Paths start with `/api/households/{householdId}/spending`.

| Method | Path | Behaviour |
| --- | --- | --- |
| POST | `/records` | Capture purchase/refund, optionally remembering selected prices. 201 for creation, 200 for a matching retry. |
| GET | `/records` | Bounded history page with full receipts. |
| GET | `/records/{recordId}` | Read a full record, including retained voided history. |
| PUT | `/records/{recordId}/header` | Correct date, receipt total, reference and notes. |
| PUT | `/records/{recordId}/lines/{lineId}` | Correct actual values/promotion/notes, optionally remembering prices. |
| PUT | `/records/{recordId}` | Deliberately replace merchant/header/lines, optionally remembering prices. |
| POST | `/records/{recordId}/void` | Void using the current revision, retaining history. |
| GET | `/items/{itemId}/last-paid-price` | Lookup using merchantId, currencyCode, onOrBefore and optional excludeRecordId. |
| GET | `/currencies` | Scoped discovery of supported capture currency codes/minor units. |

Creation includes a Location for GET. Mutations/reads otherwise return 200. Missing
last-paid matches return 204, while missing/foreign resources return 404. There are
no delete/unvoid endpoints. Invalid input returns 400; duplicate create identities,
stale record/price revisions and edits to voided records return 409 Problem Details
with traceId. Record conflicts have title `Spending record revision conflict`.

## Capture contracts

Quick purchase example (IDs are illustrative):

```json
{
  "id": "11111111-1111-1111-1111-111111111111",
  "merchantId": "22222222-2222-2222-2222-222222222222",
  "spendingDate": "2026-11-17",
  "currencyCode": "GBP",
  "kind": "Payment",
  "lines": [{
    "expenseCategoryId": "33333333-3333-3333-3333-333333333333",
    "netAmount": 12.50
  }]
}
```

The client generates/preserves id before the first request and retries with the
same values. A matching retry returns the record's current state, including edits
or voiding, without replaying price updates. Changed create values conflict. The
service computes the create hash; it is neither accepted from nor returned to clients.

Header id, merchantId, spendingDate, currencyCode, kind and nonempty lines are
required. Kind is Payment or Refund; originalPaymentId is optional only for a
refund. Lines require category and actual NetAmount, including explicit zero.
Missing amounts never become zero. Catalogue item, quantity/unit price, unit data,
descriptions/references, promotion status and notes remain optional as documented
in the service/domain guides. Quantity and unit price must be supplied together;
linked items supply their own unit identity. Category splits and mixed detail work
without catalogue items. Receipt total/reference/notes are optional on creation.

PromotionStatus defaults to Unknown on creation. Enum values use single defined
text labels, case-insensitively: Payment/Refund and Unknown/Yes/No. Numbers, numeric
strings, undefined labels and comma-combined labels are rejected. Converters are
local to these contracts and do not change existing snapshot/setup enum protocols.

Optional rememberPrices chooses linePosition, chosenUnitPrice and optional
expectedRevision. Null/omitted price revision means create only; an existing usual
price requires its current revision. Choices save atomically with the receipt.
There is no automatic price adoption or inference from promotion flags.

## Corrections and returned data

All mutations require expectedRevision. Header correction requires spendingDate and
explicit nullable receiptTotal, externalReference and notes; null clears them.
Line correction requires NetAmount, explicit nullable quantity/unitPrice/notes and
an explicit promotionStatus. Replacement requires merchantId, date, lines and all
three nullable header fields. Both line correction and replacement can include
rememberPrices. Void requires only expectedRevision.

Unknown JSON members are rejected at every request level. Clients cannot overwrite
household IDs, server hashes, copied names, generated line IDs or immutable
currency/kind/original-payment association. Those identity mistakes require void
and a new capture as established by the domain slice.

Responses contain full header metadata, currency precision, revisions, timestamps,
void status and ordered lines with copied metadata and actual amounts. They include
derived totalAmount, countedAmount, receiptDifference and reconciliationStatus.
Refund counted amounts are negative; voided records count zero while retaining
their raw total/lines. OriginalPaymentIsVoided is null without a linked original,
false for an active original Payment and true if that Payment has subsequently
been voided. This is current descriptive status, not a change to refund amounts
or the refund's own revision. It is populated for detail, replay, mutation and
history responses with a household-scoped batch over distinct linked originals;
mapping a history page never fetches each original separately. The status read
may be newer than the page's receipt read, rather than a frozen historical label.
No completeness or mixed-currency conversion is inferred.
Last-paid responses distinguish nominal unit price from effective price after
discounts and never update usual prices. Setup API contracts remain unchanged.

## Bounded history contract

ListPageAsync backs GET history; the older unbounded internal ListAsync is not
exposed. Query options: inclusive from/to dates, includeVoided (default false),
pageSize (default 25, maximum 100) and cursor. Responses contain records,
nextCursor (null at the end) and pageSize; there is no total-count scan.

Order is spending date, creation time and UUID descending. A cursor contains a
versioned anchor and is bound to household, date filters, void selection and page
size. Follow nextCursor using the same query options. Malformed/oversized cursors,
changed context and invalid page sizes return 400. Cursor data is opaque navigation
state, not an authorisation token; all queries still apply household scope.

PostgreSQL row comparison performs the keyset seek. EF applies LIMIT to parent
records in a subquery before joining their lines, with at most one additional
parent as bounded lookahead. Receipts are never split across pages. One SQL read
keeps the selected receipt aggregates consistent; linked original-payment status
is read separately as described above. Continuation is not a frozen multi-page snapshot.
Intervening edits that move records in the ordering may change later page membership.

## Verification

At completion of the initial API slice, all 10 new paging cases and 45 new HTTP cases passed. Coverage includes generated
SQL parent-limit placement, complete receipts, equal-date/time continuation,
cursor scope/filter binding, page/date validation, 201 versus 200 retries through
correction/void, zero versus missing amounts, malformed nested values, enum labels,
household isolation, receipt replacement, stale revisions, atomic price adoption,
refunds and last-paid/currency discovery.

At that checkpoint, full backend verification passed: 1,208 tests (777 data/domain, 431 HTTP API), with
no failures or skipped tests. The run builds all backend projects and includes
migration/model and fixed-hash compatibility checks. No dependencies were added;
the existing NU1900 warning remains for unavailable NuGet vulnerability metadata.

Review follow-up adds one scoped status-batch service case and seven HTTP cases:
one verifies multiple linked refunds use one batched status query without changing
their amounts/revisions; six exercise missing linePosition/chosenUnitPrice, empty
expectedRevision, zero/negative and absent line positions on both capture and line
correction, proving 400 responses with unchanged records/prices. The existing refund
detail/replay test also verifies that a later original-payment void is labelled.
All 317 spending-filtered tests pass (213 data/domain, 104 HTTP).
Full backend verification after the follow-up passes all 1,216 tests (778
data/domain, 438 HTTP API), with no failures or skipped tests.

Next: budget domain and persistence using the agreed dated replacement plans.
UI remains deferred until backend workflows are established.
