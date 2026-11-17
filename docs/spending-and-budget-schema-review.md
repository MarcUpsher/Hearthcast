# Spending and budget schema — review handoff

17 November 2026. Review of `spending-and-budget-schema-plan.md`, followed by discussion with the user. This file is input for another agent to revise the planning document; it does not change that document or authorise implementation.

## Agreed product direction

The immediate purpose is understanding and optimising everyday spending, primarily for the user's UK life. The user wants to capture known regular outgoings, see what food/eating out/etc. actually cost, and choose what to budget for them. This is not debt remediation, a complete accounting system, or a recreation of QuickBooks.

The user's South African property involves rental income, mortgage payments, tax and expenses. Modelling that accounting is outside the immediate scope. Do not encode a UK-only restriction: UK use is the initial context, not a schema boundary.

The most important design instruction is to make as few assumptions as possible, following the existing financial snapshot approach. Users define categories, merchants, spending items and budget items. The schema supplies flexible primitives and integrity guarantees; configuration supplies their meaning.

- Do not build in UK/SA, mandatory/discretionary, or particular expense classifications.
- Named commitments and broad category allowances should use the same budget-entry mechanism.
- Amount, currency and frequency are explicit data, not inferred from a name or category.
- Actual spending and planned amounts remain separate.
- If a mortgage/loan outgoing is recorded, the user is interested in the full payment. Principal/interest and debt balances are handled in existing financial snapshot/net-worth functionality. Mortgage support is not a priority or special workflow.
- Income tracking, savings allocations, cash-flow forecasting and affordability calculations are deferred. An earlier review suggestion to add these was explicitly narrowed back after discussion; do not expand the plan around it.
- “Should spend on food” means a user-chosen allowance informed by spending history, not a system-prescribed recommendation.
- Fixed/variable and mandatory/discretionary are different dimensions. Neither should control behaviour implicitly. Reconsider required `CostBehaviour = Fixed | Variable`: omit it initially or retain it as optional descriptive metadata. The user agreed with this overall direction; the exact field decision remains for the plan revision.

## Overall assessment

The draft is fundamentally sound. Preserve its separation of spending, effective-dated budgets and financial snapshots; quick capture with optional itemisation; explicitly updated usual prices; copied historical metadata; exact decimal values; currency-separated totals; and atomic revision-checked saves.

The existing `SnapshotConfiguration` was checked and supports the proposed approach through household-scoped alternate keys, a revision concurrency token and restricted deletion. This was a focused schema review, not an exhaustive codebase audit.

The following are review recommendations, not individually confirmed user requirements unless identified above. Resolve routine implementation choices with simple defaults rather than adding product scope.

## Issues to resolve in the planning document

### 1. Define counting boundaries without creating an accounting engine

Purchases and later credit-card repayments can otherwise count the same spending twice. Own-account transfers and cash withdrawals pose similar problems.

Recommended documentation convention: record purchases as spending; do not count settlements/transfers/withdrawals again. Cash purchases count when recorded. Avoid automatic classification based on category names or a complex transfer subsystem. The user may intentionally record a full loan payment as an outgoing; explain the intended reporting scope rather than impose principal/interest accounting.

### 2. Make budget entries explicitly additive

The draft warns about overlapping category allowances and named commitments, but cannot determine whether £500 Food includes a £50 food subscription or is additional to it.

Recommended v1 rule: entries add together. Show the category total clearly during editing. A user intending £500 overall can enter £450 general allowance plus £50 subscription. Do not infer containment, automatically subtract commitments, or add a separate category-total layer. This is a numerical semantic, not a hard-coded expense classification.

### 3. Support historical creation despite current retirement/setup changes

The lifecycle says retired items are omitted, but an item retired today may belong in a backdated January plan. Current metadata may also be inappropriate for that historical plan.

Allow deliberate inclusion of retired items in historical plans. Distinguish copying source metadata from refreshing current defaults, and state which is the normal behaviour. Do not silently rewrite saved historical data.

### 4. Resolve source links versus correction/deletion

`SpendingItemMerchantPrice.SourceSpendingLineId` may reference a line that disappears when a quick record is replaced with itemised lines. An informational provenance link must not accidentally prevent normal receipt correction.

Recommended behaviour: preserve the copied remembered price and clear its optional source reference when that source is deleted. Specify compatible database/application behaviour for household-scoped composite references. Also define deletion policies for original-payment refund links and budget carry-source links; do not blindly apply one policy to all links.

### 5. Define missing entries and completeness

Zero versus null is already clear. An absent entry is less clear: intentional exclusion, accidental omission, or an item created after the plan was saved?

Recommended rule: completeness is evaluated against the saved plan entries. Adding a setup item later must not retroactively make old plans incomplete. During plan creation, make additions and omissions visible and support deliberate exclusion; do not necessarily add a new persisted status if the existing full-replacement semantics suffice. Distinguish a null amount on an included entry from absence of that entry.

### 6. Defer or clarify the specific-budget-item actual link

`SpendingLine.BudgetItemId` points to mutable setup, while the draft requires saved-category compatibility. Which category applies if the budget item was reclassified: current setup or the budget entry effective on the spending date? What happens if no applicable entry exists?

Recommendation: defer this optional association initially. Category-based comparison serves the primary goal. If retained, define temporal category compatibility explicitly and ensure later setup edits do not invalidate historical lines.

### 7. Normalise units used for price identity

Free-text `UnitLabel` in the usual-price uniqueness key can split `kg`, `KG` and `kilogram` into different histories. Choose a canonical unit identity or an explicit normalisation convention, preserving a user-friendly label. Keep this small; no conversion engine is needed. Pack size/meaning must remain stable.

### 8. Define create idempotency separately from edit concurrency

Revision checks prevent stale edits, but a retried create can duplicate a purchase. A client-generated record UUID with defined retry semantics is a reasonable solution. Do not make receipt references unique to solve this: legitimate repeated or missing references are possible.

### 9. Specify reconciliation-save behaviour

“Surface differences” between `ReceiptTotal` and line totals does not specify whether saving is allowed. Recommendation: allow a visibly unreconciled saved record so entry can be completed later; totals continue to use lines only. This was suggested to the user, not explicitly confirmed as an individual decision.

### 10. Distinguish data correction from real refunds

Accidental duplicate purchases require correction/removal, not a compensating refund. Define a delete or void workflow and how it interacts with references. A refund represents money actually returned. Avoid assuming retirement of setup identities also describes deletion policy for actual records.

### 11. Specify enforceable invariants

In addition to the draft's existing constraints, spell out:

- Nonnegative planned amounts, positive interval counts, and valid frequency/enum values.
- Consistency between `ValueOrigin`, null amounts, confirmation dates and carry sources.
- Refund-original links target another Payment in the same household, rather than self or another Refund.
- Child-line changes advance/check the parent spending record revision; entry changes likewise participate in the budget snapshot's concurrency boundary.
- Currency/unit normalisation and supported numeric precision, ranges and rounding.
- Integrity enforcement split between database constraints and transactional application validation where cross-row checks are required.

Do not turn this into a speculative exhaustive validation subsystem. Choose constraints that protect the agreed data semantics.

## Keep deferred

- Mid-period target allocation and due-date schedules: decide before historical variance charts, not before basic capture.
- Bank reconciliation and completeness guarantees.
- Income, savings-allocation and disposable-surplus modelling.
- SA rental/property/tax accounting.
- Receipt LLM ingestion and attachments, as already deferred in the draft.
- Person allocations and detailed budget-item matching unless later use demonstrates a need.

GBP as a convenient entry default was suggested during discussion, but was not explicitly confirmed. Do not replace explicit currency support or hard-code GBP into the schema.

## Suggested revision approach

Keep the existing core model and implementation slices. Update the purpose/boundaries to emphasise user-configured everyday spending and budgeting. Resolve additive totals, historical creation, completeness, corrections and source-link behaviour. Remove behavioural assumptions attached to descriptive classifications. Separate firm invariants from deferred reporting choices and optional metadata. Do not add income or accounting tables as a consequence of this review.

## Follow-up review of the revised plan

The revised main planning document was reviewed after the initial handoff. It correctly incorporates the product direction and almost all findings: user-defined setup, removal of CostBehaviour, deferred income/accounting and individual-budget-item matching, additive budgets, historical creation, completeness, reconciliation, voiding, units and concurrency. No core schema redesign is recommended.

The following remaining findings and subsequent user decisions supersede conflicting recommendations earlier in this handoff. Update the main planning document accordingly.

### 1. Budget provenance must not block ordinary corrections — agreed

Finding: the revised reference/removal policy prevents removing or changing the currency/frequency of an entry once later plans reference it. This makes an old mistake harder to fix merely because the plan has been copied.

Recommendation accepted by the user: treat budget source links as informational and allow them to be cleared when a correction/removal would otherwise invalidate them. Preserve the independent saved values and metadata of descendants; never rebuild or rewrite their amounts automatically.

Revise the restrictive carry-source policy accordingly. Coordinate this with provenance invariants: detaching a source must not falsely mark an unchanged descendant amount as newly Confirmed. Define how CarriedForward retains its meaning when its optional source reference has been cleared (for example, permit a detached carry source). Household isolation and valid references still apply whenever links are present.

### 2. Retain old budgets; replacement plans are the normal remedy — user direction

Finding: physically deleting a BudgetSnapshot also removes its CreationRequestHash, so a delayed retry of its original create could recreate the deleted plan. This conflicts with the stated retry protection.

User response: “Not sure I'll ever want to delete an old budget. If there's a problem, create a new one.”

Planning resolution: do not offer physical deletion of budget snapshots in v1. Retain old plans and their create identities/hashes. A new effective-dated replacement plan is the normal way to change the budget. Remove the paragraph permitting deletion of unreferenced erroneous budget snapshots; no separate deletion/idempotency subsystem is needed for this scope.

This does not by itself require removing the existing revision-checked correction capability, which was discussed and accepted in point 1. Distinguish correcting saved data from creating a replacement plan. A replacement still follows the one-plan-per-effective-date rule and changes only its effective interval; it does not erase the historical plan.

### 3. Editing a carried amount confirms it — agreed

Finding: the revised document specifies confirming an unchanged carried value and clearing an amount, but does not explicitly cover changing a carried £200 amount to £250.

Recommendation accepted by the user: explicitly entering or changing a nonnull amount makes the entry Confirmed, records its confirmation date, and clears CarriedFromBudgetEntryId. Clearing the amount continues to make it Unrecorded. Do not leave an explicitly changed amount marked CarriedForward.

### 4. Minor wording consistency

The refund paragraph says “positive line amounts”, while the general spending rules allow zero. Change this to “nonnegative line amounts” for consistency. This is a review correction, not a new product requirement.

Only this review handoff was updated after the user's answers; the main planning document remains for the other agent to revise.
