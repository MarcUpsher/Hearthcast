# Snapshot exchange rates

New snapshots save an independent exchange-rate lookup date: the UTC calendar day
of capture, even if the financial date is an earlier day or a month. This date is
retained on edits. A fixed base is the alphabetically first currency present at
capture (or the first currency later added to an initially empty snapshot).

Rates are requested from Frankfurter v2's direct ECB endpoint. A dated request
returns the latest available publication on or before that date. We save each
rate's publication date, source and retrieval timestamp separately. One base
unit multiplied by the saved rate gives quote-currency units. Future converted
summaries can cross-convert through this base; this slice leaves native-currency
summaries unchanged.

The financial transaction commits before the HTTP request. All missing quotes
must pass validation before they are stored together in a separate transaction.
An unavailable provider, timeout, malformed or incomplete response leaves the
financial snapshot saved and its rates Pending. If rate storage fails after the
financial commit, the service logs the failure and reloads the actual database
state before responding. A readable database therefore returns the saved snapshot
rather than reporting a failed financial save; an ambiguous rate commit may have
succeeded, so the reloaded state is authoritative. An existing rate is never
refreshed. Adding another currency fetches only its missing base quote using the
original lookup date. Single-currency snapshots retain that date without making
an HTTP request.

Snapshot GET/create/amend responses add `exchangeRateLookupDate`,
`exchangeRateBaseCurrency`, `exchangeRateStatus` and `exchangeRates`. Status is
NotCaptured for legacy snapshots, NotRequired for snapshots needing no
conversion, Pending for missing quotes, and Ready when quotes are present.
Rate enrichment changes the snapshot revision. Concurrent edits take precedence
and discard stale fetched rates; clients should always use the returned revision.

`POST /api/households/{householdId}/snapshots/{snapshotId}/exchange-rates/retry`
accepts `{ "expectedRevision": "<saved GUID>" }` and returns the snapshot. It is
idempotent when rates are already complete. A stale revision returns 409. The
snapshot browser shows pending status, retry and saved rate details.

Existing snapshots receive no lookup date or rates during migration or edits.
The historical backfill uses the first day of month-only snapshots,
and the recorded date of day-precision snapshots, with the same on-or-before
publication rule. Converted summaries are a subsequent slice.

Provider documentation: https://frankfurter.dev/ (v2, direct provider routes).
The GBP/ZAR lookup for 2023-01-01 was verified against the public endpoint: it
returned the 2022-12-30 publication. Automated tests use fake providers/HTTP
handlers and disposable PostgreSQL databases, without sending personal data.

## Historical backfill command

After applying migrations, run:

```sh
docker compose run --rm --no-deps api --backfill-exchange-rates
```

For host development, supply the normal `ConnectionStrings__Hearthcast` setting:

```sh
dotnet run --no-launch-profile --project backend/src/Hearthcast.Api -- --backfill-exchange-rates
```

The command processes snapshots chronologically across households without starting
an HTTP server. Legacy lookup dates are initialized once from the snapshot date
(month precision uses the first). Existing lookup dates and rates are preserved,
including dates captured when creating newer snapshots. Each snapshot commits
separately and currency requests run sequentially. Re-running skips complete
snapshots and retries missing rates. It never changes item values, copied metadata,
allocations, financial dates or carry-forward history. Rate initialization and
capture update revision/timestamps, so reload open editors after running it.

Progress is followed by a JSON report containing each snapshot ID/date, lookup
date and status. Exit code 0 means complete; 2 means unresolved snapshots remain
(e.g. unavailable rates or concurrent edits); cancellation returns 130. Unexpected
database failures stop the command; committed work remains resumable. Back up the
database before running this administrative command.
