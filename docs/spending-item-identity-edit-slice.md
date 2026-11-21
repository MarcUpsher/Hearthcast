# Spending item code and name corrections

Item maintenance now allows changing an overly specific code/name to a generic
identity. Saving details updates both catalogue labels and all linked historical
spending lines, including refunds and voided receipts. The editor explains this
effect before saving. No automatic renaming or merging of existing items occurs.

The item UUID and unit key remain stable. Codes retain the existing normalization,
64-character limit and household uniqueness (including retired items). Duplicate
codes return a conflict without saving any catalogue or receipt changes. A former
code becomes available for another item after renaming; historical links continue
to use the original item's UUID.

The optional API `code` field preserves the existing code when omitted or null.
Blank or malformed values are rejected. Existing service callers can omit code
while name changes still update history. Only an actual normalized code/name
change triggers historical updates. Notes, category and unit-label edits alone
preserve older divergent copied labels and do not advance receipt revisions.

Updates run in one serializable transaction with the existing retry policy.
Affected receipts receive a new revision and update timestamp so stale receipt
edits are rejected. Saving unchanged labels does not touch receipt revisions.
Amounts, dates, categories, receipt text, units, line UUIDs, discounts, creation
request hashes and usual-price source links remain unchanged. Category/unit-label
changes keep their existing historical-copy policy. No migration is needed.

Tests cover HTTP and editor contracts, duplicate/invalid codes, household isolation,
refund/void history, multiple linked lines, retained price lookups and sources,
stale revisions, create-request retries, transaction rollback and concurrent capture.

## Verification

- Astra reviewed once. Its finding about metadata-only edits rewriting older
  divergent labels was fixed by checking normalized code/name changes, with a
  regression test. No second review was requested.
- Backend spending tests: 276 Data and 150 API tests passed against disposable
  PostgreSQL databases. NuGet vulnerability-data retrieval still reports the
  existing network warning; it did not prevent build or test completion.
- All 660 frontend tests passed. The HTTP price test now enters its date through
  the enabled date input after merchant selection, avoiding reliance on the day
  the test runs. Production web build and API/web container builds passed.
- Local containers deployed healthy after a database backup. Read-only HTTP
  comparisons confirmed all 39 existing items, 8 receipts and usual prices were
  unchanged by deployment. The served item editor matches the tested build;
  purchase list/detail URLs and scoped report reads passed smoke checks. Actual
  renames are left for the user to choose and save through item maintenance.
