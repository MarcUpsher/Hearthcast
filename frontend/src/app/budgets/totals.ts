import { Component, input } from "@angular/core";
import { BudgetEntry, BudgetItemKind, budgetRecurrence } from "./models";
import { planMoney, planTotals } from "./plan-data";

export type BudgetTotalEntry = Pick<BudgetEntry, "kind" | "expenseCategoryId" | "expenseCategoryName" | "plannedAmount" | "currencyCode" | "frequency" | "intervalCount">;
@Component({ selector: "app-budget-totals", template: `
  <div class="groups">@for (group of groups(); track group.name) { <div><h3>{{ group.name }}</h3>
    @for (total of totals(group.entries); track total.currency + total.frequency + total.interval) {
      <p><strong>{{ total.unknown ? 'Known subtotal: ' : 'Total: ' }}{{ money(total.amount, total.currency) }}</strong> · {{ recurrence(total.frequency, total.interval) }}@if (total.unknown) { · {{ total.unknown }} unknown amount(s) }</p>
    } @empty { <p class="muted">No included entries.</p> }
  </div> }</div>
  @if (byCategory()) { <details><summary>Category totals (commitments + allowances)</summary>
    @for (category of categories(); track category.id) { <h4>{{ category.name }}</h4>
      @for (total of totals(category.entries); track total.currency + total.frequency + total.interval) {
        <p>{{ total.unknown ? 'Known subtotal: ' : 'Total: ' }}{{ money(total.amount, total.currency) }} · {{ recurrence(total.frequency, total.interval) }}@if (total.unknown) { · {{ total.unknown }} unknown amount(s) }</p>
      }
    }
  </details> }
`, styles: `.groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); gap: 16px; } summary { cursor: pointer; }` })
export class BudgetTotals {
  readonly entries = input.required<BudgetTotalEntry[]>();
  readonly byCategory = input(false);
  readonly totals = planTotals;
  readonly money = planMoney;
  readonly recurrence = budgetRecurrence;
  groups() {
    return (["Commitment", "Allowance"] as BudgetItemKind[]).map(kind => ({ name: kind === "Commitment" ? "Regular commitments" : "Spending allowances", entries: this.entries().filter(entry => entry.kind === kind) }))
      .concat([{ name: "Combined earmarked", entries: this.entries() }]);
  }
  categories() {
    const groups = new Map<string, { id: string; name: string; entries: BudgetTotalEntry[] }>();
    for (const entry of this.entries()) {
      if (!groups.has(entry.expenseCategoryId)) groups.set(entry.expenseCategoryId, { id: entry.expenseCategoryId, name: entry.expenseCategoryName, entries: [] });
      groups.get(entry.expenseCategoryId)!.entries.push(entry);
    }
    return [...groups.values()].sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
  }
}
