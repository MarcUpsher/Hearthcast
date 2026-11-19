# Regular outgoings setup UI

18 November 2026. Adds Regular outgoings at `/spending/outgoings`, linked from the
shared Spending navigation. Uses the existing household-scoped budget setup API;
no backend, schema, dependency or application database changes.

## Behaviour

Create reusable outgoing definitions for rent, utilities, clubs and subscriptions.
The form requires name, immutable code, shared spending category, currency and an
Every/Period recurrence. New definitions default to GBP, every one month, while
both remain selectable. Week/Month/Year and positive integer intervals support
weekly, fortnightly, quarterly, annual and other intervals without calendar or
proration assumptions. Merchant and notes are optional under a disclosure.

The right edit sidebar changes defaults and explicitly sends null to clear a
merchant or notes. Setup retains its existing last-writer-wins contract; no
snapshot revision is invented. Historical unsupported currencies can be retained
only on their original item; new choices use the supported spending currency
policy. Retired categories/merchants remain available with visible labels.

Search covers names/codes, category, merchant, currency, notes and recurrence.
Category and Active/Retired/All filters combine. Retirement/restoration use only
the outgoing's own flag and never delete a definition. Retiring a category does
not hide its active outgoings. Edits/retirement do not change saved plan amounts,
classification, recurrence, provenance or revisions.

Amounts are absent from setup. Dated outgoing plan capture/history and expected
amounts are the following UI slice; actual-versus-plan comparisons remain deferred.

## State, contracts and recovery

Creation/details/retirement use `/households/{id}/budgets/items`; categories,
merchants and supported currencies use the existing `/spending` lookup routes.
Every details update contains all required fields including nullable merchantId
and notes; intervalCount is sent as an integer and frequency as a named string.
Code is absent from updates. No amount, revision or plan field is submitted.

Failed saves retain complete drafts; creation and retirement errors are separate.
Successful creation resets values and form validation, including submitted/touched
state. Create and edit drafts stay separate. Route, household and browser unload
guards include both drafts and block active writes. Escape/backdrop protect dirty
sidebar edits, and rejected browser Back retains the open sidebar and input.
Reads do not block navigation. Version and household-generation checks reject late
loads and all mutation completions after context changes, including leaving and
returning to the same household. Forced context changes close the sidebar.

## Verification

Component and shell tests exercise exact payloads, rendered controls, validation,
recurrence boundaries, historical currency retention, explicit null clearing,
retirement, filters, empty/retry states, reset regressions, navigation/sidebar focus
and household/request races. Astra reviewed once and identified two minor issues:
invalid codes/intervals needed field-level correction guidance, and an obsolete
retirement alert could survive a new save. Both are fixed with regression checks.
The recurrence validator is shared by form validation and submit eligibility.
Browser access is unavailable; rendered Material harness tests replace live visual
inspection. Test writes use mocked transport, not the application database.

After review fixes, **410 frontend tests** across 23 files pass under
`TZ=Europe/London`; production build and whitespace checks pass. Backend tests
were not rerun because backend code, contracts and schema are unchanged.

The [expected payment date follow-up](budget-payment-date-slice.md) adds optional
reference dates, timing descriptions and next expected dates from current setup
recurrence, including short-month and leap-year handling.
