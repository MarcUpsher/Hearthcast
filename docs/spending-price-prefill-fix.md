# Prefill prices from purchase history

20 November 2026. Selecting a previously purchased item could still leave the
price blank because only explicitly saved usual prices were automatic defaults.
History was displayed behind a separate Use this price action. Recent live checks
found valid recorded prices with no matching usual prices for the items checked.

The revised entry policy is:

1. A matching saved usual price has priority and fills immediately, independently
   of the historical lookup's completion.
2. If no usual price is found, or its lookup fails, use the last eligible recorded
   nominal unit price as an editable suggestion. The UI identifies it as a last
   recorded suggestion and asks the user to check the current purchase price.
3. If neither read supplies a price, leave manual entry available. Retry price
   lookup repeats both reads.

The existing history endpoint still scopes by household, item, merchant, currency,
stable unit and purchase-date cutoff, excluding the edited record. There is no
cross-merchant/currency fallback. Known promotions are labelled; receipt discounts
remain explicitly unallocated to individual products. Effective line prices are
shown separately and are not silently substituted for nominal unit prices.
Historical promotion status is not copied into today's purchase.

This changes the entry suggestion, not remembered-price persistence. Selecting an
item or saving its actual price does not create or update a usual price. Explicit
Use this price remains available, including when overriding a saved usual-price
suggestion. User-entered unit prices and manual line amounts survive late reads;
saved edit values remain untouched. Date, item, merchant, currency, household,
line/request generation and lock guards continue to reject stale suggestions.

The two price reads run concurrently, with automatic history application waiting
until usual-price absence/failure is known. A suggestion arriving while an inline
item dialog locks the form is deferred until that dialog closes. Deferred actions
recheck context, manual edits and locks, and are discarded on reset. Creating a
different item invalidates the old suggestion; other untouched lines can resume.

Astra reviewed once and found no blocking issue in ordinary selection or price
precedence. Sol added its suggested earlier usual-price assertion, rendered
autocomplete/input coverage, and a fix/regressions for dialog-suppressed prices.
Tests cover both lookup orders, unavailable usual prices, manual overrides,
promotions/discounts, stale contexts, saved editing and dialog cancellation.
Final verification passes all **654 frontend tests** across 33 files under
`TZ=Europe/London`, with no failures or skips. Production builds and whitespace
checks pass. Backend tests are not rerun because backend code, API contracts and
schema are unchanged. No dependencies were added.

The web app is deployed and healthy without restarting the API/database. Live
read-only checks verified the exact tested main and purchase bundles, creation
and eight direct receipt URLs, and scoped receipt/report reads. The earlier price
inspection confirmed recorded prices were available for 39 recent item/merchant/
currency combinations, all without saved usual prices. No historical receipts or
usual-price records were changed by the fix or verification.
