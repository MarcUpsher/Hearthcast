# Spending UI: next implementation slices

18 November 2026. The backend for the agreed first version is implemented and
reviewed: spending setup, capture/corrections/voids, dated budget plans and recorded
spending totals by category/currency over arbitrary inclusive dates. Budget
comparisons and historical target allocation remain deferred. The first
[categories and merchants UI slice](spending-setup-ui-slice.md) is implemented;
the implemented and planned follow-ups are listed below.

## First slice: categories and merchants (implemented)

Let the selected household create, rename, retire and restore spending categories
and merchants. These are the prerequisites for quick spending capture. Catalogue
items and remembered prices are optional later enhancements.

Use a feature-owned spending setup page, naturally under `/spending/setup`, with
one Spending entry in the app navigation. Follow the existing Angular Material
theme and setup interaction patterns: inline creation, one Edit action opening a
right sidebar, Cancel discarding the edit, and retirement as a separate action.
Provide usable loading, empty, error/retry and active/retired states. Do not seed
hard-coded categories or merchants. Expense categories are a separate domain from
the existing asset/debt classification categories; keep their models and API paths
distinct even when reusing visual patterns.

All API paths below start with `/api/households/{householdId}/spending`:

| Operation | Categories | Merchants | Request |
| --- | --- | --- | --- |
| List | GET `/categories?includeRetired=true` | GET `/merchants?includeRetired=true` | None |
| Create | POST `/categories` | POST `/merchants` | `{ "name": "..." }` |
| Rename | PUT `/categories/{id}/name` | PUT `/merchants/{id}/name` | `{ "name": "..." }` |
| Retirement | PUT `/categories/{id}/retirement` | PUT `/merchants/{id}/retirement` | `{ "isRetired": true }` or false |

Responses contain `id`, `householdId`, `name` and `isRetired`. Creates return 201;
updates return 200. Reuse the existing API error formatting for Problem Details.
There is no delete operation. Renaming or retirement does not rewrite saved
spending history. See [setup API documentation](spending-setup-api-slice.md) and
the current backend contracts/controllers for the full rules.

Integrate with `Workspace` for household selection. Capture the household identity
for every in-flight request, and prevent stale responses from a previous household
from replacing the current household's rows or applying mutations to its UI state.
Protect unsaved edits on navigation and household changes using the existing
`canLeave`/route-guard conventions. A rejected save retains the user's entered name
and displays the error; loading a page never writes setup records.

## Implementation and review organisation

The agreed workflow uses Sol high as the main builder/coordinator and Astra low
as an independent reviewer. Sol implements and tests the agreed slice, then Astra
reviews the completed changes once. Sol assesses the findings, fixes valid issues,
reruns relevant checks, commits and pushes. There is no automatic second review.
After delivery, discuss and agree on the next slice before starting it.

Only the builder edits product code. The reviewer focuses on user workflows,
household-switch races, unsaved edits, request contracts and failure recovery, and
returns concrete findings to the builder. The coordinator owns integration and
verification; delegation does not relax acceptance criteria. Local `review.md`
remains ignored when used for manual reviews.

Useful existing frontend references:

- `frontend/src/app/core/api.ts`, `api-errors.ts` and `workspace.ts`.
- `frontend/src/app/app.ts` and `app.routes.ts` for navigation/household guards.
- `frontend/src/app/setup/` for setup/sidebar behaviour and related tests.
- `frontend/src/app/snapshots/editor.ts` for navigation/unsaved-change conventions.
- `frontend/README.md` and `docs/project-layout.md` for current UI conventions.

Prefer a small feature component with feature-owned models and nearby template,
styles and tests. Reuse existing transport and state conventions; no new global
state framework, dependency or backend/schema change is expected for this slice.

## Acceptance and verification

Users can manage both lists, find retired entries, restore them, cancel edits and
recover from failed loads/saves without losing entered values. Tests must cover
these behaviours, household isolation/switching during pending requests, navigation
protection and correct request payloads. Validate labels, keyboard operation,
sidebar focus, narrow layouts and error presentation in the rendered UI.

Run the relevant component tests during development, then `npm test` and
`npm run build` from `frontend/`. Report actual results and any limitations. Do not
migrate the application database as part of frontend work. The user subsequently
authorized rebuilding/updating the running web service after reviewed UI delivery;
use the web-only Compose command without restarting backend/database services.

## Item catalogue and usual prices (implemented)

The user chose catalogue maintenance before purchase capture. The
[item and price UI](spending-items-ui-slice.md) provides searchable item setup,
metadata edits, retirement/restoration and explicit merchant/currency usual prices
with revision-aware conflict recovery. Items is the Spending landing page; category
and merchant setup remains accessible through Spending navigation.

## Following slices

1. Spending capture and bounded history: date, merchant, currency, quick category
   amounts/splits and optional catalogue-item lines with quantity and actual price.
   Usual prices prefill entry but are overridable; actual amounts never silently
   replace remembered prices. Include errors/retries, receipt reconciliation and
   voiding accidental entries. Refund creation/editing remains separate.
   Use the established client-generated capture ID for safe retries. See
   [record API](spending-record-api-slice.md). The
   [capture/history UI](spending-capture-ui-slice.md) is implemented, including
   both quick amounts and catalogue item lines. Saved payment correction is now
   implemented through the shared entry form; see [purchase editing](spending-edit-ui-slice.md).
   [Inline item creation](spending-inline-item-ui-slice.md) is implemented on
   each purchase line. Refund creation/editing remains deferred.
2. Category reporting (implemented): month/week/custom date controls translated to explicit
   inclusive backend dates; separate currencies, recorded-spending wording and
   visible reconciliation information. See
   [summary API](spending-category-summary-api-slice.md) and
   [category-report UI](spending-summary-ui-slice.md).
3. [Regular outgoing setup UI](budget-setup-ui-slice.md) is implemented with
   metadata, recurrence, retirement and shared category/merchant choices.
   [Expected payment dates](budget-payment-date-slice.md) provide optional schedule
   references and next-payment displays. [Dated outgoing plans and amounts](budget-plans-ui-slice.md)
   are implemented, including carry-forward capture, history, exact totals by
   currency/recurrence and saved amount corrections. Actual-vs-plan comparisons still require
   a later agreed allocation policy.

## Form usability follow-up (implemented)

The [spending form usability plan](spending-form-usability-plan.md) records the
approved changes: simplify sold-by metadata and hide generated unit keys,
make item versus category-amount lines explicit, add name-first item search with
linked category filtering/defaults, and keep receipt notes compact. Existing saved classifications and unit identities are preserved.
