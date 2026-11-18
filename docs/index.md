# Hearthcast documentation

Start with the current references below. Files named `*-slice.md` record incremental
implementation decisions and verification at that time; they are not a chronological
specification to reconstruct. Some are still useful feature references, while the
original single-currency projection and initial screen notes are superseded.

## Running and contributing

- [README](../README.md): capabilities and a fresh Docker Compose start.
- [Containers and PostgreSQL](containers-and-postgres.md): configuration, migrations,
  isolated backend tests, backups and optional host debugging.
- [Frontend README](../frontend/README.md): development server, production build and tests.
- [Project layout](project-layout.md): responsibilities, dependencies and conventions.
- [Historical import](historical-import.md): optional import of already normalised data.

## Current behaviour and contracts

| Area | Start here |
| --- | --- |
| Ownership and household attribution | [Schema design](relational-schema-plan.md), [individual holdings](individual-holdings-summary-slice.md), [household summaries](household-summary-slice.md) |
| Snapshot capture, carry-forward and correction | [Lifecycle](snapshot-lifecycle-slice.md), [capture API](snapshot-api-slice.md), [lifecycle API](snapshot-lifecycle-api-slice.md) |
| Reporting and currency completeness | [Full summary](snapshot-summary-slice.md), [merged summary](merged-summary-slice.md), [saved exchange rates](snapshot-exchange-rates-slice.md) |
| Historical graphs | [Historical summary series](historical-summary-series-slice.md) |
| Modelling, presets and result interpretation | [Current modelling guide](modelling.md) |
| Current projection input shape and native currency pots | [Native currency projections](native-currency-projection-slice.md) |
| Expense milestones | [Expense targets](projection-expense-target-slice.md) |
| Spending setup domain and persistence | [Spending setup slice](spending-setup-slice.md) |
| Spending setup service operations and concurrency | [Spending setup services](spending-setup-service-slice.md) |
| Spending setup HTTP contracts and endpoints | [Spending setup API](spending-setup-api-slice.md) |
| Spending category/merchant setup screen and draft protection | [Spending setup UI](spending-setup-ui-slice.md) |
| Spending item catalogue and explicit usual-price maintenance | [Items and prices UI](spending-items-ui-slice.md) |
| Spending-record domain and persistence | [Spending records](spending-records-slice.md) |
| Spending-record services, retries and price provenance | [Record services](spending-record-service-slice.md) |
| Spending-record HTTP capture and bounded history | [Record API](spending-record-api-slice.md) |
| Recorded spending by category/currency over an inclusive date range | [Category-summary service](spending-category-summary-service-slice.md) |
| Recorded spending date-range HTTP contract | [Category-summary API](spending-category-summary-api-slice.md) |
| Budget domain, provenance and persistence | [Budget domain slice](budget-domain-slice.md) |
| Budget setup service operations | [Budget setup services](budget-setup-service-slice.md) |
| Budget setup HTTP contracts and endpoints | [Budget setup API](budget-setup-api-slice.md) |
| Budget capture, retries, corrections and as-of reads | [Budget snapshot services](budget-snapshot-service-slice.md) |
| Budget snapshot HTTP contracts, history and effective reads | [Budget snapshot API](budget-snapshot-api-slice.md) |
| Growth attribution | [Growth breakdown](projection-growth-breakdown-slice.md) |
| Setup-to-reporting HTTP example | [Workflow verification](http-workflow-verification.md) |

Frontend wire contracts live with their owning features in `frontend/src/app/`.
Snapshot capture/browsing/modelling share `snapshots/models.ts`. Numeric snapshot
codes and named setup/projection codes retain their endpoint-specific JSON forms.
Backend controllers and their `Contracts/` folders define the actual HTTP surface;
there are no stored summary/projection documents to migrate when reporting changes.

## Design and implementation history

[Spending and budget schema plan](spending-and-budget-schema-plan.md) describes
purchase/payment tracking and dated replacement budget plans. Implemented layers
include [setup domain/persistence](spending-setup-slice.md),
[setup services](spending-setup-service-slice.md), [setup API](spending-setup-api-slice.md),
[record domain/persistence](spending-records-slice.md) and
[record services](spending-record-service-slice.md) and
[record API/bounded history](spending-record-api-slice.md) and
[budget domain/persistence](budget-domain-slice.md) and
[budget setup services](budget-setup-service-slice.md) and
[budget snapshot services](budget-snapshot-service-slice.md) and
[budget setup API](budget-setup-api-slice.md) and [budget snapshot API](budget-snapshot-api-slice.md).
The [recorded spending category-summary service](spending-category-summary-service-slice.md)
implements inclusive date-range totals by category/currency with reconciliation
information. The [category-summary API](spending-category-summary-api-slice.md)
exposes it through a required date range. Its [original handoff](spending-category-summary-service-handoff.md)
records the service scope. [Spending setup UI](spending-setup-ui-slice.md) is
implemented; purchase/reporting UI and budget comparisons remain later work.

The [spending UI plan](spending-ui-plan.md) records the categories/merchants screen
scope, API contracts, implementation/review ownership and acceptance criteria,
followed by planned capture, reporting and optional item detail.

The [initial schema proposal](schema-proposal.md) and [relational plan](relational-schema-plan.md)
explain the original model. Household, classification, tracked-item, financed-asset,
snapshot and reporting slice notes retain their decisions and historical test counts.
Those counts describe their individual implementation passes, not today's complete suite.

The original [calculator](projection-calculator-slice.md),
[preset persistence](projection-presets-slice.md), [preparation](projection-preparation-slice.md),
[HTTP](projection-http-slice.md) and [screen](modelling-screen-slice.md) notes include
single-currency or pre-preset behaviour. Use the current modelling/native-currency
references above for present behaviour and payloads. [Preset management](modelling-preset-management-slice.md)
records the later save/default/draft-protection decisions.

Historical notes are retained for reasoning and migration history. Their old request
examples and file lists should not be copied as current contracts.
