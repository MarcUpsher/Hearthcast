# Recorded spending category-summary API

18 November 2026. Exposes the verified [category-summary service](spending-category-summary-service-slice.md)
through one read-only, household-scoped endpoint. This completes the backend path
from spending capture to category totals over a chosen range. There are no schema
changes, new dependencies or running-app/database deployment in this slice.

## Route and validation

```http
GET /api/households/{householdId}/spending/summary?from=2026-12-01&to=2026-12-31
```

Both query dates are required and inclusive. Supply dates as `yyyy-MM-dd`.
Single-day ranges and arbitrary spans across weeks, months and years are valid.
The backend does not interpret calendar labels such as `December`, choose current
dates, restrict results to a month or prorate amounts. The UI will translate its
date-range controls into these two dates.

Missing/empty/malformed dates, impossible calendar dates, default `0001-01-01`
dates and reversed ranges return **400 Validation Problem Details** with field
errors and `traceId`. A well-formed request for an unknown household, including
the empty GUID, returns **404 Problem Details**. A known household with no eligible
records returns **200** with empty `categories`/`warnings` and zero counts. The
route exposes GET only; POST, PUT and DELETE return **405**.

## Response contract

For one reconciled GBP Food purchase of 12.50:

```json
{
  "householdId": "11111111-1111-1111-1111-111111111111",
  "from": "2026-12-01",
  "to": "2026-12-31",
  "categories": [
    {
      "expenseCategoryId": "22222222-2222-2222-2222-222222222222",
      "categoryName": "Food",
      "currencyCode": "GBP",
      "currencyMinorUnits": 2,
      "netAmount": 12.50
    }
  ],
  "reconciliationRecordCounts": {
    "notChecked": 0,
    "reconciled": 1,
    "unreconciled": 0
  },
  "warnings": []
}
```

Amounts are JSON numbers produced from exact decimals. Category rows retain their
saved category ID and copied name. One row combines all included lines less receipt discounts for a
category/currency even if historical saved precisions differ. `currencyMinorUnits`
is the maximum saved precision among those lines; the exact `netAmount` is not
rounded. There is no cross-currency
grand total. Rows sort by currency, copied display name and category ID. The
service selects the most recent saved name within each category/currency group;
see its documentation for tie-breaking. Current setup names/classification do not
rewrite older records.

Each warning contains `spendingRecordId`, `spendingDate`, saved `merchantName`,
`currencyCode`, `receiptTotal`, `lineTotal` and `difference`. It appears once per
included mismatched receipt, even if that receipt spans multiple categories.
`lineTotal` retains its name but represents the net recorded total after receipt
discounts. `difference` is receipt total minus that unsigned total, including refunds.
Warnings do not change category totals.

The fixed count fields count **records**, not lines or categories, across all
included currencies. `notChecked` means no receipt total was supplied;
`reconciled` means the supplied receipt total matches the recorded net total; `unreconciled`
means it differs. All three fields are present even when zero. They describe
receipt reconciliation, not whether every purchase has been captured.

## Calculation and implementation boundary

The controller delegates to `SpendingCategorySummaryService` and maps explicit
HTTP response records; it contains no reporting arithmetic. Date validation follows
the existing nullable-required request pattern. Program registers the service
through the same scoped DI conventions as the capture services. Cancellation is
passed through to the service.

Saved line `NetAmount` is authoritative for quick amounts, category splits,
discount overrides and itemised captures. Payments add; refunds subtract on their
own recorded date, including when a linked original lies outside the range or was
later voided. Voided payments and refunds are excluded. Zero and negative category
totals remain valid. Category budgets are not required. Every eligible record in
the range contributes; this report is independent of history pagination.

Use the label **recorded spending**. Neither empty results nor reconciled receipts
establish complete spending coverage. No FX conversion, budget comparison, target
allocation, calendar presets or UI is added. Report reads do not mutate saved
receipts or prices. Existing capture/setup routes retain their contracts.

## Verification

`SpendingSummaryApiTests` contains 24 PostgreSQL-backed HTTP cases. The focused run
passed all 24 with no failures/skips. It exercises setup and capture through HTTP,
then summary retrieval with mixed detail, overridden item prices, separate
currencies, signed refunds, void exclusion, copied metadata, reconciliation
warnings/counts, inclusive boundaries and arbitrary date ranges. Other cases cover
the explicit JSON contract, corrections and unchanged record state after reads,
empty reports, household isolation, all date validation paths, not-found responses
and unsupported mutation methods.

The full API suite also passed: **655 tests, zero failures or skips**, including
all 24 new summary cases, in approximately 3 minutes 5 seconds. Focused results:
`/tmp/hearthcast-summary-api/spending-summary-api.trx`; full API results:
`/tmp/hearthcast-summary-api/api-suite.trx`. Tests use disposable PostgreSQL
databases, not the application database. The known NuGet `NU1900`
vulnerability-feed network warning appeared during the focused build. Whitespace
checks passed. Existing budget snapshot and summary-service work was preserved
during implementation.

The final review follow-up removed saved precision from the service's grouping
key, retaining one exact category/currency total with maximum saved display
precision. All **24 summary HTTP tests** and **21 summary-service tests** passed
after that change. Follow-up TRX files are in
`/tmp/hearthcast-summary-review-followup/`; the full API count above records the
preceding full-suite run.

The subsequent [backend completion checkpoint](spending-and-budget-schema-plan.md#backend-completion-checkpoint)
records the final full solution run after all review fixes: 990 Data tests and
655 API tests passed, with no failures or skips.

## Receipt discount extension

The [receipt discounts and last-price slice](receipt-discounts-and-last-price-slice.md)
extends this contract with category deductions, net recorded totals and explicit
historical price assistance. Earlier verification counts above describe their
original checkpoints.
