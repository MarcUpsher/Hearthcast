import { Component, DestroyRef, computed, effect, inject, signal, untracked } from "@angular/core";
import { FormsModule, NgForm } from "@angular/forms";
import { RouterLink } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { localToday, spendingDate } from "../spending/amounts";
import { SpendingSetupEntry } from "../spending/models";
import { SpendingNavigation } from "../spending/navigation";
import { BudgetPaymentDate } from "./payment-date";
import { nextPaymentDate, validPaymentAnchor } from "./payment-schedule";
import { BudgetItemKind, CurrentOutgoing, CurrentOutgoings, budgetRecurrence } from "./models";
import { BudgetTotals } from "./totals";
import { parseBudget, planMoney, planTotals } from "./plan-data";

@Component({
  imports: [FormsModule, RouterLink, MatButtonModule, MatInputModule, MatSelectModule, SpendingNavigation, BudgetPaymentDate, BudgetTotals],
  templateUrl: "overview.html", styleUrl: "overview.scss",
})
export class OutgoingOverview {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private home: string | null = null;
  private generation = 0;
  private version = 0;
  private destroyed = false;
  readonly asOf = signal(localToday());
  readonly report = signal<CurrentOutgoings | null>(null);
  readonly categories = signal<SpendingSetupEntry[]>([]);
  readonly merchants = signal<SpendingSetupEntry[]>([]);
  readonly loading = signal(false);
  readonly error = signal("");
  readonly search = signal("");
  readonly status = signal<"all" | "active" | "retired">("all");
  readonly category = signal("");
  readonly date = spendingDate;
  readonly money = planMoney;
  readonly recurrence = budgetRecurrence;
  readonly kinds: BudgetItemKind[] = ["Commitment", "Allowance"];
  readonly entries = computed(() => this.report()?.items.flatMap(row => row.entry ? [row.entry] : []) ?? []);
  kind(row: CurrentOutgoing) { return row.entry?.kind ?? row.item.kind; }
  visibleKind(kind: BudgetItemKind) { return this.visible().filter(row => this.kind(row) === kind); }
  readonly totals = computed(() => planTotals(this.report()?.items.flatMap(row => row.entry ? [row.entry] : []) ?? []));
  readonly missing = computed(() => this.report()?.items.filter(row => !row.entry).length ?? 0);
  readonly unknown = computed(() => this.report()?.items.filter(row => row.entry?.plannedAmount === null).length ?? 0);
  readonly categoryOptions = computed(() => {
    const categories = new Map<string, string>();
    for (const row of this.report()?.items ?? []) categories.set(this.categoryId(row), this.categoryName(row));
    return [...categories].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
  });
  readonly visible = computed(() => {
    const search = this.search().trim().toLocaleLowerCase();
    return (this.report()?.items ?? []).filter(row => (this.status() === "all" || row.item.isRetired === (this.status() === "retired")) &&
      (!this.category() || this.categoryId(row) === this.category()) &&
      [this.kind(row) === "Commitment" ? row.item.name : "", this.kind(row) === "Commitment" ? row.item.code : "",
        this.name(row), this.kind(row) === "Commitment" ? this.code(row) : "", this.categoryName(row), this.merchantName(row) ?? "",
        (row.entry ? row.entry.notes : row.item.notes) ?? "", row.entry?.currencyCode ?? row.item.currencyCode, this.state(row)]
        .some(value => value.toLocaleLowerCase().includes(search)))
      .sort((a, b) => this.name(a).localeCompare(this.name(b)) || this.code(a).localeCompare(this.code(b)) || a.item.id.localeCompare(b.item.id));
  });
  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => {
        if (home === this.home) return;
        this.home = home; this.generation++; this.version++;
        this.report.set(null); this.categories.set([]); this.merchants.set([]); this.loading.set(false); this.error.set("");
        this.search.set(""); this.status.set("all"); this.category.set(""); this.asOf.set(localToday());
        if (home) void this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
  }
  private current(home: string, generation: number, version: number) {
    return !this.destroyed && this.w.current()?.id === home && generation === this.generation && version === this.version;
  }
  async load(form?: NgForm) {
    const home = this.home, generation = this.generation, asOf = this.asOf();
    if (!home || this.w.current()?.id !== home || this.destroyed) return;
    if (form?.invalid || !asOf || !validPaymentAnchor(asOf)) { this.error.set("Choose a supported as-of date."); return; }
    const version = ++this.version; this.loading.set(true); this.error.set("");
    try {
      const [text, categories, merchants] = await Promise.all([
        this.api.getText(`households/${home}/budgets/current-outgoings?asOf=${asOf}&includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/categories?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/merchants?includeRetired=true`),
      ]);
      if (this.current(home, generation, version)) {
        this.report.set(parseBudget<CurrentOutgoings>(text)); this.categories.set(categories); this.merchants.set(merchants);
        if (this.category() && !this.categoryOptions().some(category => category.id === this.category())) this.category.set("");
      }
    } catch (error) { if (this.current(home, generation, version)) this.error.set(message(error)); }
    finally { if (this.current(home, generation, version)) this.loading.set(false); }
  }
  async today() { this.asOf.set(localToday()); await this.load(); }
  name(row: CurrentOutgoing) { return this.kind(row) === "Allowance" ? this.categoryName(row) : row.entry?.itemName ?? row.item.name; }
  code(row: CurrentOutgoing) { return row.entry?.itemCode ?? row.item.code; }
  categoryId(row: CurrentOutgoing) { return row.entry?.expenseCategoryId ?? row.item.expenseCategoryId; }
  categoryName(row: CurrentOutgoing) { return row.entry?.expenseCategoryName ?? this.categories().find(category => category.id === row.item.expenseCategoryId)?.name ?? "Category unavailable"; }
  merchantName(row: CurrentOutgoing) {
    return row.entry ? row.entry.merchantName : row.item.merchantId ? this.merchants().find(merchant => merchant.id === row.item.merchantId)?.name ?? "Merchant unavailable" : null;
  }
  state(row: CurrentOutgoing) {
    return !this.report()?.snapshotId ? "No effective plan" : !row.entry ? "Not included in plan" : row.entry.plannedAmount === null ? "Unknown amount" : "Amount recorded";
  }
  nextDate(row: CurrentOutgoing) {
    return this.kind(row) === "Allowance" || row.item.isRetired ? null : nextPaymentDate(row.item.paymentAnchorDate, row.item.frequency, row.item.intervalCount, this.report()!.asOf);
  }
  scheduleDiffers(row: CurrentOutgoing) {
    return !!row.entry && (row.entry.frequency !== row.item.frequency || row.entry.intervalCount !== row.item.intervalCount);
  }
  afterPlan(row: CurrentOutgoing) {
    const next = this.nextDate(row), end = this.report()?.effectiveUntilExclusive;
    return !!row.entry && !!next && !!end && next >= end;
  }
}
