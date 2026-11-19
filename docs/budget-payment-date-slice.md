# Expected outgoing payment dates

19 November 2026. Adds an optional payment reference date to regular outgoing
setup and a calendar-based next-expected-date display. Amounts, dated plan capture,
bank execution/matching and historical cash-flow allocation remain separate work.

## Stored context and compatibility

`BudgetItem.PaymentAnchorDate` is a nullable DateOnly stored in PostgreSQL as a
nullable date column. It represents a known scheduled occurrence, not a moving
next-date field and not a recorded actual payment. Existing rows start unset;
there is no inferred date or data backfill. Domain/API validation and a database
check reject the PostgreSQL infinity sentinel endpoints, accepting 0001-01-02
through 9999-12-30 or null. Retirement preserves the value.

The date is current setup metadata, paired with the item's current recurrence.
It is not copied into or inferred from saved BudgetEntry metadata. Changing it
does not modify saved plan entries, amounts, provenance, revisions or create
hashes, or spending records. Current-outgoing reads expose it in Item alongside
its current frequency; saved Entry context remains independent. A future cash-flow
or historical schedule feature will need an explicit saved scheduling contract.

Create accepts optional `paymentAnchorDate` as yyyy-MM-dd or null. Item responses
(detail/list/create/update/retirement and nested current-item responses) expose it.
Details updates set it when supplied, clear it with explicit null, and preserve it
when omitted by an older client. Existing service overloads similarly preserve
the saved value; date-aware overloads perform the date change atomically with the
other defaults. Internal presence tracking is not a wire field. Required nullable
merchant/notes members keep their established semantics.

## Calendar behaviour

The UI labels the field Expected payment date and explains that a known date
repeats using Every / Period. It is optional on both creation and editing. Empty
values send explicit null; errors and drafts retain the entered date.

Next expected means the first occurrence on or after the displayed local as-of
date. A future anchor starts the schedule there. Weekly/fortnightly payments keep
their anchored weekday and cycle. Month/year recurrences keep their original day
and phase: 31 January becomes 28/29 February, then 31 March. A 29 February annual
anchor becomes 28 February in non-leap years and returns to 29 February in leap
years. Clipping is derived from the original anchor for every occurrence. A date
on the final day of a month does not otherwise imply an end-of-month policy.

Calculations use UTC calendar arithmetic from explicit local DateOnly strings;
no timezone conversion or DST day shift is introduced. Large intervals never
iterate through every occurrence or overflow JS dates; if none exists in the
supported range, show that explicitly. Timing descriptions show weekday, monthly
ordinal or annual month/day. Retired items retain their reference/timing metadata
without an active next-payment forecast. List search includes timing text.

The display names its as-of date and refreshes it on page load/window focus.
There are no weekend/bank-holiday adjustments or automatic changes to the stored
reference, and expected dates do not establish actual bank debit dates.

## Verification and delivery

PostgreSQL-backed tests cover create/read/edit/clear, omitted-value compatibility,
invalid date rejection, raw constraint enforcement including infinities, scoped
HTTP writes, migration preservation and unchanged saved-plan/spending context.
Calendar tests exercise short months without drift, leap/century rules, fortnight
cycles, DST boundaries, inclusive dates, early years and large interval overflow.
Rendered form/list tests exercise date payloads, clearing, draft recovery, previews
and retired display. Astra reviewed once and found that the editor still showed
a forecast for retired outgoings. The preview now stays hidden there while the
reference date remains editable, covered by a rendered regression.

The additive migration requires an API rebuild/application as well as the web
update. Take a private PostgreSQL custom-format backup before applying it, then
use the established Compose migration/health checks. Tests use disposable databases;
verification does not create spending or outgoing records in the application DB.

Full backend verification passes **998 data/domain and 663 API tests** with zero
failures/skips, including migration and saved-hash compatibility coverage. The
existing NuGet vulnerability-feed network warning appeared; no packages changed.
After the review fix, **454 frontend tests** pass and the production build and
whitespace checks pass. Live browser inspection remains unavailable; rendered
Material harnesses cover the form, list and retired editor.

Pre-migration database backup: `/tmp/hearthcast-before-payment-date-20261119-100617.dump`
(custom format, private permissions, 347723 bytes).
