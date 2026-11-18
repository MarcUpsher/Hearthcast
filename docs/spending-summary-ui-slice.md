# Recorded spending by category UI

18 November 2026. Adds Spending by category at `/spending/summary`, using the
existing read-only household summary endpoint. No new backend, dependencies,
schema, budgets or automatic allocations.

The initial report covers the user's current local calendar month. This/last month
and This/last week shortcuts resolve to explicit inclusive dates. Weeks run Monday
through Sunday; month/year selection supports any month, and From/To accept any
valid span, including a single day or multiple weeks. Date arithmetic starts from
the user's local date and uses UTC calendar operations to avoid daylight-saving
shifts; the API receives only `from=yyyy-MM-dd&to=yyyy-MM-dd`.

Tables show API-supplied net amounts under saved category identities/names, grouped
by currency with saved display precision. Zero and negative totals remain visible;
refunds subtract on their own dates and voids are excluded by the backend. The page
never derives totals from bounded history or mixes currencies. Receipt counts are
per record across currencies, not per category. Mismatched receipts appear once,
with their saved merchant/date, unsigned receipt and line totals, and signed
receipt-minus-lines discrepancy. These warnings do not alter category totals.
The summary response is read as text and numeric JSON tokens are preserved as
decimal strings before decoding; only integer count/precision metadata becomes
JavaScript numbers. Exact decimal formatting avoids losing small fractions in
large totals. Warning formatting preserves supplied decimal places because warning
responses omit their own precision field.

Pending/failed refreshes retain the preceding report under its actual returned
range. Changed range controls show an explicit apply notice; a failed initial load
is distinct from an empty successful report. Household changes clear results and
reset to the current month. Request version and household-generation guards reject
late reads/errors, including leave-and-return to the same household. This page has
no write calls, so leaving during a read requires no confirmation.

Coverage wording consistently says recorded spending. Empty or reconciled results
do not establish complete spending coverage. Budget comparisons, refund creation,
subcategories and graphs remain separate work.

Astra reviewed once and identified binary-number precision loss for large sums.
The fix preserves raw monetary tokens and includes a raw HTTP-response regression
for `1900000000000.0001` at four decimal places, plus escaped/numeric string
token tests and text-mode Problem Details error coverage. Browser access remains
unavailable; rendered Material harness tests validate inputs, buttons, tables,
messages and date-preserving output rather than live browser screenshots.

After the review fix, **340 frontend tests** across 22 files pass under
`TZ=Europe/London`; `npm run build` and whitespace checks pass. No backend tests
were rerun because backend code/contracts/schema did not change.
