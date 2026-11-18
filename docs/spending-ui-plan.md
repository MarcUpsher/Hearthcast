# Spending UI: next implementation slices

18 November 2026. The backend for the agreed first version is implemented and
reviewed: spending setup, capture/corrections/voids, dated budget plans and recorded
spending totals by category/currency over arbitrary inclusive dates. Budget
comparisons and historical target allocation remain deferred. No UI implementation
is included in this planning document.

## First slice: categories and merchants

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

One coordinator owns the scope, integration, verification and final delivery.
For a multi-agent run, give one implementation agent ownership of the new spending
feature files. The coordinator owns shared navigation/routes and agrees the model
and component exports first. Avoid concurrent edits to the same files.

Give an independent reviewer the brief and completed diff, then have the
implementer/coordinator resolve concrete findings before final verification. Focus
review on user workflows, household-switch races, unsaved edits, request contracts
and failure recovery. The coordinator remains responsible for a working combined
result; splitting work does not relax the acceptance criteria. Reviewers should
keep local findings in ignored `review.md` unless another location is agreed.

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
deploy the app or migrate the application database as part of frontend work.

## Following slices

1. Quick spending capture and bounded history: date, merchant, currency, category
   and amount, optional category splits, errors/retries and correction/void flows.
   Use the established client-generated capture ID for safe retries. See
   [record API](spending-record-api-slice.md).
2. Category reporting: month/week/custom date controls translated to explicit
   inclusive backend dates; separate currencies, recorded-spending wording and
   visible reconciliation information. See
   [summary API](spending-category-summary-api-slice.md).
3. Optional itemised capture and catalogue/remembered-price setup. Keep actual
   amounts separate from usual-price defaults; a discounted purchase does not
   silently replace a usual price.
4. Budget setup and dated plan UI can follow as a separate workflow. Actual-vs-plan
   comparisons still require a later agreed allocation policy.
