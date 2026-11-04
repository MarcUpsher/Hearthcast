# Historical summary series

`GET /api/households/{householdId}/summary-history?from=2020-01-01&to=2026-11-04&intervalMonths=3&currency=ZAR`
returns chart-ready points calculated from saved household snapshots. `from` and `to`
are inclusive. Valid calendar intervals are 1, 3, 6 and 12 months, aligned to
January. Within each interval the latest recorded snapshot is selected; an
interval without a snapshot contributes no point. A duplicate date is resolved
by creation time and then ID. No averages or fabricated carried values are
introduced by the series.

Each point includes the source snapshot ID, date and revision, household net
position, financed-asset equity, and ordinary item contributions by saved
category ID/name and Asset/Debt type. Debts have negative contributions.
It also includes each person's household-allocated category contributions,
with the display name copied into that snapshot. Personal holdings excluded
from household totals are not added to these person lines.
Financed equity appears on its own line rather than being included again in
ordinary category amounts. The frontend can choose investment category IDs
without assuming that a category is called “Investments” in every snapshot.
Clicking a point can open its source snapshot.

The requested currency can differ from currencies held in a snapshot. The
calculation uses only saved exchange rates and the same allocation, equity and
net rules as the combined snapshot summary. If the needed rate is missing, the
point has a null or partial known subtotal and missing-rate count; it is never
silently treated as zero. Each series component also carries missing-value and
carried counts. In particular, old ZAR-only snapshots are usable in ZAR now,
and report missing GBP conversion until historical GBP quotes are saved.

The endpoint reads snapshot graphs in one consistent database view for the
selected points and stores no materialized summary table. It does not fetch or
persist exchange rates. The response also lists every currency recorded by the
household, so a currency can remain available even when the selected range does
not itself contain holdings in it.

The History page uses the series to draw household Investments (selected asset
category IDs), one investment line per contributing person, Financed equity,
and Household net position. Distinct colors and line patterns help distinguish
the series. Range, interval and currency
controls reload the series; choosing investment categories updates that line
locally. Incomplete amounts are omitted from lines, and missing calendar
intervals break lines rather than joining distant points. Each plotted point
opens its saved snapshot. A short fade between responses softens chart changes;
the underlying values and dates are never interpolated into new records.

Within the recorded scope, an existing person's selected categories with no
entries total zero. Unknown recorded amounts, missing conversions and people
absent from a snapshot remain gaps. Person colors are retained by person ID
through range/name changes within the current household view. Invalid or cleared
ranges invalidate older requests before validation. All-zero data uses a nonzero
axis range so tick positions stay distinct.

History response contracts live under `Summaries/Models/History`. Merged entries
carry an explicit `MergedEntryKind` (Item or FinancedAsset), so the history
calculation does not depend on the formatting of UI keys.
