# Saved purchase editing UI

18 November 2026. Uses the existing household-scoped correction contracts; no
schema, service or API changes.

From Purchases & history, select View details then Edit purchase. A fresh detail
read loads the current revision. The shared entry form restores saved actual
amounts without recalculating them or fetching new usual prices. Existing saved
labels appear alongside current choices. Currency, record kind and refund identity
stay immutable; void/recreate is the correction path for those mistakes. Refund
editing/creation remains a separate UI slice.

A header-only edit uses the header correction endpoint. A single line's actual
amount/quantity/unit price/promotion/notes correction uses the line endpoint.
These preserve saved labels and line identity. Wider changes use one atomic full
replacement. The form and save button explicitly explain that replacement refreshes
merchant/category/item labels from current setup. Receipt descriptions, references,
line notes and standalone unit metadata survive replacement. No save updates usual
prices. Saved currency precision governs validation, including historical currencies.

Validation failures retain an editable draft. Conflicts and uncertain responses
retain the draft but block another save until an explicit reload. Reload requires
confirmation before discarding local changes and adopts the freshly read revision;
it never automatically retries the user's correction. A voided record cannot be
reopened for saving. Cancel, route navigation, household changes and browser unload
protect dirty/uncertain edits, and active writes block navigation. Late operations
from a previous record/household cannot populate the current draft.

Astra reviewed once. Findings addressed: compare normalized persisted values rather
than decimal spelling/UI flags when choosing corrections, propagate editor reloads
to selected details (including voided state), and allow navigation during stalled
detail reads. Regression tests cover each case. Browser access is
unavailable in this session; rendered Angular Material harness tests exercise
controls and actions, but no live visual inspection is claimed.

After fixes, all **287 frontend tests** pass under `TZ=Europe/London`, and
`npm run build` passes. No backend or application database changes.
# Purchase-page navigation update

The [purchase pages slice](spending-purchase-pages-slice.md) moves editing into a
dedicated receipt page, opened read-only by default. Save and Cancel return to its
saved view. Existing correction, revision and recovery contracts remain in use.
