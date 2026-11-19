import { Component, computed, input } from "@angular/core";
import { SpendingCurrency } from "../spending/models";
import { spendingDate } from "../spending/amounts";
import { reportMoney } from "../spending/summary-data";
import { BudgetEntry } from "./models";
import { monthlyAverages } from "./monthly-average";

@Component({ selector: "app-monthly-budget-summary", template: `
  <section aria-labelledby="monthly-average-title">
    <h3 id="monthly-average-title">Monthly planning average</h3>
    <p>Based on the plan effective on {{ date(asOf()) }}. Totals include every saved entry, including retired definitions, and stay unchanged by the list filters.</p>
    <p class="muted">A planning average spreads costs across months. The payment schedule determines when money is due. Mid-month plan changes and later plans take effect separately.</p>
    @if (averages().length) {
      <div class="table-wrap"><table>
        <caption>Monthly planning averages by currency</caption>
        <thead><tr><th scope="col">Currency</th><th scope="col">Regular commitments</th><th scope="col">Spending allowances</th><th scope="col">Combined earmarked</th></tr></thead>
        <tbody>@for (row of averages(); track row.currency) { <tr>
          <th scope="row">{{ row.currency }}</th>
          @for (amount of [row.commitments, row.allowances, row.combined]; track $index) { <td>
            @if (amount.amount === null) { <strong>Unknown</strong> }
            @else { @if (amount.unknown) { <span>Known subtotal</span><br> }<strong>{{ money(amount.amount, row.currency, row.places) }}</strong><span> / month</span> }
            @if (amount.unknown) { <p class="muted">{{ amount.unknown }} unknown amount(s)</p> }
          </td> }
        </tr> }</tbody>
      </table></div>
      @for (row of averages(); track row.currency) {
        @if (row.precisionFallback) { <p class="muted">{{ row.currency }} display precision is unavailable; its planning averages use six decimal places.</p> }
        @if (row.roundingDifference) { <p class="muted">{{ row.currency }} combined earmarked uses unrounded values. Displayed subtotals differ slightly because of rounding.</p> }
      }
    } @else { <p>This plan includes no commitments or allowances to average.</p> }
    <details><summary>Calculation basis</summary>
      <p>Weekly: amount × 52 ÷ (12 × number of weeks). Monthly: amount ÷ number of months. Annual: amount ÷ (12 × number of years).</p>
      <p>Uses the saved amounts and recurrence throughout this plan. Weekly costs use 52 weeks per year; individual months and calendar years can contain different numbers of payments. For example, GBP 600 annually averages GBP 50/month, while the payment itself remains GBP 600.</p>
      <p>Allowances are additional to listed commitments. Currencies remain separate. Figures are rounded once per displayed subtotal or combined total using the current currency display precision; original saved amounts remain available below.</p>
    </details>
  </section>
`, styles: `
  .table-wrap { overflow-x: auto; margin: 20px 0; }
  table { width: 100%; border-collapse: collapse; }
  caption { text-align: left; margin-bottom: 12px; }
  th, td { padding: 12px; border-bottom: 1px solid var(--mat-sys-outline-variant); text-align: left; vertical-align: top; }
  td { font-variant-numeric: tabular-nums; white-space: nowrap; }
  td p { margin: 6px 0 0; } summary { cursor: pointer; }
  @media (max-width: 550px) { th, td { padding: 12px 6px; } }
` })
export class MonthlyBudgetSummary {
  readonly entries = input.required<BudgetEntry[]>();
  readonly currencies = input<SpendingCurrency[] | null>(null);
  readonly asOf = input.required<string>();
  readonly averages = computed(() => monthlyAverages(this.entries(), this.currencies()));
  readonly money = reportMoney;
  readonly date = spendingDate;
}
