# Budget commitments and spending allowances — slice plan

19 November 2026. Approved implementation brief; see the [implemented slice](budget-allowances-slice.md)
for behaviour, review and verification. This is the implementation brief
for the next slice, following [outgoing plans](budget-plans-ui-slice.md) and the
[outgoings overview](outgoings-overview-ui-slice.md). It extends the existing
budget domain, services and API before changing the UI.

## Intended result

One budget view, with two sections:

- **Regular commitments:** rent, internet, utilities, clubs and subscriptions.
  Amounts may vary; commitment does not mean a fixed price.
- **Spending allowances:** category-based amounts earmarked for Food, Pharmacy,
  Eating out, Miscellaneous and similar discretionary or variable spending.

An allowance looks like **Food · GBP 400 per month**. The user selects a spending
category and enters an amount. Currency defaults to GBP and the period to every
one month; other supported currencies and Week/Month/Year intervals remain
available under optional details. No visible code, separate name, merchant or
payment date is needed for an allowance.

Both sections use **one dated replacement plan**, one effective date, one revision
and the existing carry-forward behaviour. A monthly allowance remains effective
until replaced; it does not require a new snapshot every month.

## Amount semantics

Retain the schema plan's existing additive rule. An allowance is additional to
separately listed commitments, not an inclusive category ceiling. For example:

- Food allowance GBP 400/month plus a Food subscription GBP 50/month earmarks
  GBP 450/month for Food.
- To earmark GBP 400 overall including that subscription, enter GBP 350 allowance
  plus the GBP 50 commitment.

Explain this beside allowances and show category subtotals while editing. Never
infer containment from names or silently subtract commitments from an allowance.
This avoids introducing two competing editable totals for one category.

Show commitment subtotals, allowance subtotals and combined earmarked totals in
matching currency/frequency/interval buckets. Keep different buckets separate.
Unknown amounts make the relevant subtotal and combined total visibly partial;
known zero, included unknown and omitted entry remain distinct. Do not introduce
monthly equivalents, exchange conversion or calendar-period allocation here.

## Backend: classification and persistence

1. Add `BudgetItemKind` with stable values `Commitment = 1`, `Allowance = 2`.
   Persist `Kind` on reusable `BudgetItem` and independently on saved `BudgetEntry`.
   Add it to resolved metadata and metadata value types, setup/entry DTOs and
   frontend wire models. Named strict JSON enum handling follows existing budget
   enums; invalid labels/numbers/nulls are rejected when supplied.
2. Kind is immutable on a setup identity in this slice. Creation chooses it;
   ordinary details updates do not convert an item. A mistaken type can be
   retired and replaced. Reclassification/migration tooling is separate work.
   Explicit saved metadata must agree with the resolved item's kind; it cannot
   turn a commitment into an allowance through the metadata API.
3. Existing setup items and saved entries migrate to Commitment. Do not infer
   allowances from names or categories: an existing Food item might be a real
   recurring payment. Preserve all existing amounts, revisions, timestamps,
   confirmation dates, identities, source links and creation hashes.
4. Add enum check constraints. Allowance definitions reject merchant/payment date;
   saved allowance entries reject merchant identity/name. Enforce these in the
   domain/service and database. Amounts remain on dated entries, never on setup.
5. Support one active allowance definition per household/category/currency,
   regardless of recurrence. Use a filtered unique index and a scoped conflict
   response to protect against concurrent creation/restoration/default changes.
   Retired identities and historical plans remain valid. Multiple commitments in
   the same category are allowed.
6. A plan may include at most one allowance per saved category/currency, even
   through distinct retired/replacement identities. Enforce it on create, add,
   full correction and metadata correction, with a filtered unique entry index
   as a persistence backstop. This prevents two allowance rows from appearing to
   be competing category targets. Currency/frequency buckets still control totals.

## Backend: services and transport

Use the existing household-scoped budget item/plan endpoints rather than adding
a parallel target aggregate or second plan lifecycle.

- Extend setup creation with optional `kind`, default Commitment for old callers.
  Commitment creation keeps its existing required code/name contract. Allowance
  creation takes category, currency, frequency, interval and optional notes. The
  service generates a unique immutable internal code and initial category-based
  name; do not generate identifiers from mutable category names. Reject supplied
  merchant/payment date and user code/name for the allowance variant. Validate
  category ownership and existing currency/recurrence rules as usual.
- Saved/current category labels provide the allowance's visible title. Its
  internal setup name/code remain available for compatibility, without requiring
  a second user-maintained allowance name. Category renames do not rewrite saved
  plan labels. Ordinary amount changes never recreate its setup identity.
- Expose kind in setup, detail, history, effective, copy-source and
  current-outgoings responses. Carry resolves kind from the selected saved source;
  new entries resolve it from setup. The grouped UI uses saved kind when an entry
  exists, and setup kind otherwise.
- Preserve the existing Confirm/Carry/Unrecorded intents. Carry compatibility must
  include kind. Changing an allowance's category is also a meaning change: it
  requires explicit amount confirmation or Unknown, not a metadata-only edit or
  carrying a Food number into Pharmacy. Extend both domain compatibility checks
  and service incoming-link detachment checks consistently. Commitments retain
  their existing category-edit policy.
- Preserve old clients: omitted create kind means Commitment; ordinary setup
  updates preserve stored kind. Missing kind on explicit historical metadata
  means legacy Commitment. An allowance's explicit metadata must include its
  actual kind. Source/setup-resolved entries obtain kind without another client
  field. Unknown JSON members and other strict nested validation remain intact.
- Preserve persisted create idempotency. Keep the **exact version-one canonical
  hash** for requests whose explicit metadata is absent or legacy Commitment,
  including an explicitly supplied default kind. Use a versioned canonical form
  including kind when explicit Allowance metadata is supplied. Version selection
  depends only on the frozen request, never mutable setup/source state. Do not
  add a field to every old canonical hash or rewrite stored hashes. Replay tests
  must prove pre-migration creates still match after deployment.
- Continue household scoping, transaction/revision checks, atomic rollback,
  source detachment and sanitized 400/404/409 responses. Map both new unique
  constraints explicitly rather than reporting a generic server error.

## UI: keep the everyday view small

Reuse existing routes and navigation slots. Rename the visible overview and plan
labels to **Budget overview** and **Budget plans**; existing URLs stay usable.
Regular outgoings remains the setup screen for commitments and filters out
allowances. Do not add another top-level allowance setup tab.

The plan editor has Commitments and Spending allowances sections under the same
date/notes header and Save plan action. Save always includes both sections, even
when one is collapsed or filtered. Source loading and unknown/zero/carry states
retain their existing semantics for both kinds. Retired source rows stay visible
until deliberately omitted. Clearly flag duplicate retired/replacement allowance
rows and require choosing one before save.

Add **Add spending allowance** inside the allowance section. Choose a category;
optional currency/period details default to GBP/month. Resolve/reuse an existing
active allowance for that category/currency, or create its reusable definition
through the allowance variant of setup creation. Add/select its draft row and
enter the amount there. In normal use the row shows the category, amount and
period; unknown/confirmation/context controls can live under details. Editing an
amount confirms it on the explicit local date; unchanged carries keep their old
confirmation date. Preserve full decimal transport and validation.

Definition creation is an explicit setup action, like inline catalogue creation:
it creates no saved amount or plan. Cancelling the plan can leave an unused
definition; do not claim atomic setup-and-plan saving. If creation has an
uncertain result or a duplicate conflict, reload scoped definitions and reconcile
the matching category/currency before retrying. Never create a second active
allowance or discard the rest of the plan draft.

Provide edit-defaults and retire/restore controls for allowance definitions within
the existing budget screen, under details rather than a new navigation page.
Retirement does not remove saved entries or amounts. Do not expose type conversion
or arbitrary category reassignment in this initial allowance UI. Existing
commitment editing and narrow saved amount corrections continue to work.

The overview/history/details group saved rows by their recorded kind. Show
allowance amount/period/category without a payment schedule, merchant or a
"payment date not set" warning. Only commitments show current schedule context.
Filtering/search remain consistent with displayed saved metadata; totals cover
all included entries and label their scope. Reuse current dirty, write-blocking,
household-generation and stale-response protections, including any allowance
dialog/default edit. No user data is seeded or automatically classified.

## Verification and delivery order

Implement from the bottom up, with the backend complete before the UI:

1. Domain/schema/migration and persistence tests: enum/default/backfill rules,
   null merchant/date invariants, active allowance uniqueness, per-plan duplicate
   protection, carry meaning rules and unchanged existing data/revisions/hashes.
2. Service/API contracts and PostgreSQL tests: old payload compatibility, strict
   enums, generated identity, household scope, concurrent duplicates, restore and
   update conflicts, mixed plans, source metadata, revision/rollback/detachment,
   all read responses and exact old/new create-hash replay behaviour.
3. UI and rendered tests: lean create/edit/retire flow; both saved sections and
   unchanged values surviving a partial-section edit; GBP/month defaults; exact
   decimals; subtotal/combined/unknown rules; category overlap wording; no schedule
   for allowances; saved kind/labels after setup changes; failed/uncertain saves,
   dialog/navigation/household guards and late completions.
4. Run affected tests, then full backend/frontend regression and production build.
   Astra reviews once; Sol fixes valid findings and reruns affected verification,
   without an automatic second review. Commit/push after review fixes. Privately
   back up the application database before the additive migration, deploy API and
   web together, and check deployed schema/read contracts/bundles without writing
   test records into the live household.

The deliverable is a usable budget containing real commitments and category
allowances in one dated plan, with preserved historical meaning and simple entry.
Actual-spending reconciliation/comparisons, income/affordability, category caps,
automatic debit matching, period proration, monthly-equivalent totals, cash-flow
forecasts and receipt changes are outside this slice.
